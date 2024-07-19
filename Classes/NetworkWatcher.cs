using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Network;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using WoLightning.Types;
using static WoLightning.Types.ChatType;


namespace WoLightning
{
    public class NetworkWatcher
    {
        public bool running = false;
        Plugin Plugin;
        Preset ActivePreset;


        private IPlayerCharacter? LocalPlayer;
        private uint lastHP = 0;
        private uint lastMaxHP = 0;
        private uint lastMP = 0;
        private bool wasDead = true;
        private uint lastVulnAmount = 0;
        private uint lastDDownAmount = 0;
        private int lastStatusCheck = 0;
        private int lastPartyCheck = 0;
        private int lastCheckedIndex;
        private readonly bool[] deadIndexes = [false, false, false, false, false, false, false, false]; //how do i polyfill
        private int amountDead = 0;

        IPlayerCharacter? IPlayerCharacter;
        IPlayerCharacter? MasterCharacter;
        private Timer lookingForMaster = new Timer(new TimeSpan(0, 0, 5));
        private int masterLookDelay = 0;

        public bool isLeashed = false;
        public bool isLeashing = false;
        private Timer LeashTimer = new Timer(new TimeSpan(0, 0, 1));

        private int DeathModeCount = 0;

        readonly private string[] FirstPersonWords = ["i", "i'd", "i'll", "me", "my", "myself", "mine"];

        public NetworkWatcher(Plugin plugin)
        {
            Plugin = plugin;
            Plugin.ClientState.Login += HandleLogin;
        }

        public void Start() //Todo only start specific services, when respective trigger is on
        {
            running = true;
            LeashTimer.Elapsed += (sender, e) => CheckLeashDistance();
            //LeashTimer.Start(); 650 // 305

            Plugin.Framework.Update += checkLocalPlayerState;

            Plugin.ChatGui.ChatMessage += HandleChatMessage;
            Plugin.ClientState.TerritoryChanged += HandlePlayerTerritoryChange;
            Plugin.EmoteReaderHooks.OnEmoteIncoming += OnEmoteIncoming;
            Plugin.EmoteReaderHooks.OnEmoteOutgoing += OnEmoteOutgoing;
            Plugin.EmoteReaderHooks.OnEmoteSelf += OnEmoteSelf;
            Plugin.EmoteReaderHooks.OnEmoteUnrelated += OnEmoteUnrelated;
        }
        public void Stop()
        {
            if (LeashTimer.Enabled) LeashTimer.Stop();
            LeashTimer.Dispose();

            if (lookingForMaster.Enabled) lookingForMaster.Stop();
            lookingForMaster.Dispose();

            Plugin.Framework.Update -= checkLocalPlayerState;

            Plugin.ChatGui.ChatMessage -= HandleChatMessage;
            Plugin.ClientState.TerritoryChanged -= HandlePlayerTerritoryChange;
            Plugin.EmoteReaderHooks.OnEmoteIncoming -= OnEmoteIncoming;
            Plugin.EmoteReaderHooks.OnEmoteOutgoing -= OnEmoteOutgoing;
            Plugin.EmoteReaderHooks.OnEmoteSelf -= OnEmoteSelf;
            Plugin.EmoteReaderHooks.OnEmoteUnrelated -= OnEmoteUnrelated;
            running = false;
        }
        public void Dispose()
        {
            //Plugin.GameNetwork.NetworkMessage -= HandleNetworkMessage;

            if (LeashTimer.Enabled) LeashTimer.Stop();
            LeashTimer.Dispose();

            if (lookingForMaster.Enabled) lookingForMaster.Stop();
            lookingForMaster.Dispose();

            Plugin.Framework.Update -= checkLocalPlayerState;

            Plugin.ChatGui.ChatMessage -= HandleChatMessage;
            Plugin.ClientState.Login -= HandleLogin;
            Plugin.ClientState.Logout -= HandleLogout;
            Plugin.ClientState.TerritoryChanged -= HandlePlayerTerritoryChange;
            Plugin.EmoteReaderHooks.OnEmoteIncoming -= OnEmoteIncoming;
            Plugin.EmoteReaderHooks.OnEmoteOutgoing -= OnEmoteOutgoing;
            Plugin.EmoteReaderHooks.OnEmoteSelf -= OnEmoteSelf;
            Plugin.EmoteReaderHooks.OnEmoteUnrelated -= OnEmoteUnrelated;
            running = false;
        }

        

        public IPlayerCharacter? scanForPlayerCharacter(string playerNameFull)
        {
            var f = Plugin.ObjectTable.FirstOrDefault(x => (ulong)x.ObjectKind == 1 && ((IPlayerCharacter)x).Name + "#" + ((IPlayerCharacter)x).HomeWorld.Id == playerNameFull); //player character
            if (f != null) return (IPlayerCharacter)f;
            else return null;
        }
        public IPlayerCharacter? scanForPlayerCharacter(uint GameObjectId)
        {
            var f = Plugin.ObjectTable.FirstOrDefault(x => (ulong)x.ObjectKind == 1 && x.GameObjectId == GameObjectId); //player character
            if (f != null) return (IPlayerCharacter)f;
            else return null;
        }

        public void scanForMasterCharacter()
        {
            MasterCharacter = scanForPlayerCharacter(Plugin.Authentification.MasterNameFull);
            if (MasterCharacter != null)
            {
                if (lookingForMaster.Enabled)
                {
                    Plugin.PluginLog.Info("Found Master Signature!");
                    lookingForMaster.Stop();
                    LeashTimer.Start();
                }
            }
            else Plugin.PluginLog.Info("Could not find Signature... Retrying");
        }

        private void CheckLeashDistance()
        {
            //Plugin.PluginLog.Info("Checking Leash Distance");
            //Plugin.PluginLog.Info($"Master: {Plugin.Configuration.MasterNameFull} Sig: {MasterCharacter}");

            scanForMasterCharacter(); // Is Master still in Memory?
            if (MasterCharacter == null)
            {

                if (!lookingForMaster.Enabled)
                {
                    lookingForMaster.Elapsed += (sender, e) => scanForMasterCharacter();
                    lookingForMaster.Start();
                    LeashTimer.Stop();
                    Plugin.PluginLog.Info("Lost Master Signature!");
                    Plugin.PluginLog.Info("Starting Scanner...");
                    return;
                }
                return;
            }

            Plugin.PluginLog.Info($"{MasterCharacter.Name} - {MasterCharacter.Address} - {MasterCharacter.ObjectIndex}");
            Plugin.PluginLog.Info($"Valid: {MasterCharacter.IsValid()} Master Pos: {MasterCharacter.Position} Local Pos: {Plugin.ClientState.LocalPlayer.Position} diff: {MasterCharacter.Position - Plugin.ClientState.LocalPlayer.Position}");
        }


        private void checkLocalPlayerState(IFramework Framework)
        {
            if (LocalPlayer == null)
            {
                LocalPlayer = Plugin.ClientState.LocalPlayer;
                lastHP = LocalPlayer.CurrentHp;
                lastMaxHP = LocalPlayer.MaxHp;
                lastMP = LocalPlayer.CurrentMp;
            }

            if (lastHP != LocalPlayer.CurrentHp) HandleHPChange(); //check maxhp due to synching and such
            if (lastMP != LocalPlayer.CurrentMp) HandleMPChange();

            if (lastStatusCheck >= 60 && ActivePreset.FailMechanic.IsEnabled())
            {
                lastStatusCheck = 0;
                bool foundVuln = false;
                bool foundDDown = false;
                if (LocalPlayer.StatusList != null)
                {
                    foreach (var status in LocalPlayer.StatusList)
                    {
                        //Yes. We have to check for the IconId. The StatusId is different for different expansions, while the Name is different through languages.
                        if (status.GameData.Icon >= 17101 && status.GameData.Icon <= 17116) // Vuln Up
                        {
                            foundVuln = true;
                            var amount = status.StackCount;

                            Plugin.PluginLog.Verbose("Found Vuln Up - Amount: " + amount + " lastVulnCount: " + lastVulnAmount);
                            if (amount > lastVulnAmount)
                            {
                                Plugin.sendNotif($"You failed a Mechanic!");
                                Plugin.WebClient.sendPishockRequest(ActivePreset.FailMechanic);
                            }
                            lastVulnAmount = amount;
                        }
                        if (status.GameData.Icon >= 18441 && status.GameData.Icon <= 18456) // Damage Down
                        {
                            foundDDown = true;
                            var amount = status.StackCount;
                            if (amount > lastDDownAmount)
                            {
                                Plugin.sendNotif($"You failed a Mechanic!");
                                Plugin.WebClient.sendPishockRequest(ActivePreset.FailMechanic);
                            }
                            lastDDownAmount = amount;
                        }
                    }
                }
                if (!foundVuln) lastVulnAmount = 0;
                if (!foundDDown) lastDDownAmount = 0;
            } //Shock On Vuln / Damage Down

            if (ActivePreset.PartymemberDies.IsEnabled() && Plugin.PartyList.Length > 0 && lastPartyCheck >= 60) // DeathMode
            {
                if (lastCheckedIndex >= Plugin.PartyList.Length) lastCheckedIndex = 0;
                if (Plugin.PartyList[lastCheckedIndex].ObjectId > 0 && Plugin.PartyList[lastCheckedIndex].CurrentHP == 0 && !deadIndexes[lastCheckedIndex])
                {
                    deadIndexes[lastCheckedIndex] = true;
                    amountDead++;
                    Plugin.PluginLog.Information($"(Deathmode) - Player died - {amountDead}/{Plugin.PartyList.Length} Members are dead.");
                    Plugin.WebClient.sendPishockRequest(ActivePreset.PartymemberDies, [ActivePreset.PartymemberDies.Intensity * (amountDead / Plugin.PartyList.Length), ActivePreset.PartymemberDies.Duration * (amountDead / Plugin.PartyList.Length)]);
                }
                else if (Plugin.PartyList[lastCheckedIndex].ObjectId > 0 && Plugin.PartyList[lastCheckedIndex].CurrentHP > 0 && deadIndexes[lastCheckedIndex])
                {
                    deadIndexes[lastCheckedIndex] = false;
                    amountDead--;
                    Plugin.PluginLog.Information($"(Deathmode) - Player revived - {amountDead}/{Plugin.PartyList.Length} Members are dead.");
                }
                lastCheckedIndex++;
                lastPartyCheck = 0;
            }

            lastHP = LocalPlayer.CurrentHp;
            lastStatusCheck++;
            lastPartyCheck++;
        }

        private void HandleHPChange()
        {
            if (lastMaxHP != LocalPlayer.MaxHp)
            {
                lastMaxHP = LocalPlayer.MaxHp;
                return;
            }
            if (ActivePreset.Die.IsEnabled() && LocalPlayer.CurrentHp == 0 && !wasDead)
            {
                Plugin.sendNotif($"You Died!");
                Plugin.WebClient.sendPishockRequest(ActivePreset.Die);
                wasDead = false;
            }
            if (lastHP < LocalPlayer.CurrentHp && ActivePreset.Die.IsEnabled())
            {
                Plugin.WebClient.sendPishockRequest(ActivePreset.Die);
            }
            if (lastHP > 0) wasDead = false;
        }
        private void HandleMPChange()
        {
            // Currently Unused
        }
        private void HandleStatusChange()
        {
            //64 = vuln up
            Plugin.PluginLog.Verbose("StatusList Changed");
            Plugin.PluginLog.Verbose(LocalPlayer.StatusList.ToString());
        }

        public unsafe void HandleChatMessage(XivChatType type, int timespamp, ref SeString senderE, ref SeString message, ref bool isHandled)
        {
            if (Plugin.ClientState.LocalPlayer == null)
            {
                Plugin.PluginLog.Error("Wtf, LocalPlayer is null?");
                return;
            }
            if (message == null) return; //sanity check in case we get sent bad data

            string sender = senderE.ToString().ToLower();

            if ((int)type <= 107 && Plugin.ClientState.LocalPlayer.Name.ToString().ToLower() == sender) // its proooobably a social message
            {

                if (ActivePreset.SayBadWord.IsEnabled())
                {
                    foreach (var (word, settings) in ActivePreset.SayBadWord.CustomData)
                    {

                        if (message.ToString().ToLower().Contains(word.ToLower()))
                        {
                            Plugin.sendNotif($"You said the bad word: {word}!");
                            Plugin.WebClient.sendRequestShock(settings);
                        }
                    }
                }

                //slightly different logic
                if (ActivePreset.SayFirstPerson.IsEnabled())
                {
                    foreach (var word in message.ToString().Split(' '))
                    {
                        string sanWord = word.ToLower();
                        sanWord = sanWord.Replace(".", "");
                        sanWord = sanWord.Replace(",", "");
                        sanWord = sanWord.Replace("!", "");
                        sanWord = sanWord.Replace("?", "");
                        sanWord = sanWord.Replace("\"", "");
                        sanWord = sanWord.Replace("\'", "");
                        if (FirstPersonWords.Contains(sanWord))
                        {
                            Plugin.sendNotif($"You referred to yourself wrongly!");
                            Plugin.WebClient.sendPishockRequest(ActivePreset.SayFirstPerson);
                        }
                    }
                }
            }


            ChatTypes? chatType = GetChatTypeFromXivChatType(type);
            if (chatType == null)
            {
                return;
            }
            if (ActivePreset.Channels.Contains(chatType.Value)) //If the channel can be selected and is activated by the user
            {
                List<RegexTrigger> triggers = ActivePreset.CustomMessageTriggers;
                foreach (RegexTrigger trigger in triggers)
                {
                    Plugin.PluginLog.Information(message.TextValue);
                    if (trigger.Enabled && trigger.Regex != null && trigger.Regex.IsMatch(message.TextValue))
                    {
                        Plugin.PluginLog.Information($"Trigger {trigger.Name} triggered. Zap!");
                        Plugin.WebClient.sendRequestShock([trigger.Mode, trigger.Intensity, trigger.Duration]);
                    }
                }
            }
        }

        private void HandleLogin()
        {
            Plugin.onLogin();
            IPlayerCharacter = Plugin.ClientState.LocalPlayer;
            Plugin.ClientState.Login -= HandleLogin;
            Plugin.ClientState.Logout += HandleLogout;
        }

        private void HandleLogout()
        {
            Plugin.onLogout();
            IPlayerCharacter = null;
            Plugin.ClientState.Logout -= HandleLogout;
            Plugin.ClientState.Login += HandleLogin;
        }


        private void HandlePlayerTerritoryChange(ushort obj)
        {
            // Currently Unused
        }


        private void OnEmoteIncoming(IPlayerCharacter sourceObj, ushort emoteId)
        {
            //Plugin.PluginLog.Info("[INCOMING EMOTE] Source: " + sourceObj.ToString() + " EmoteId: " + emoteId);

            if (ActivePreset.GetPat.IsEnabled() && emoteId == 105)
            {
                Plugin.sendNotif($"You got headpatted by {sourceObj.Name}!");
                Plugin.WebClient.sendPishockRequest(ActivePreset.GetPat);
            }

        }

        private void OnEmoteUnrelated(IPlayerCharacter sourceObj, IGameObject targetObj, ushort emoteId)
        {
            //Plugin.PluginLog.Info("[Unrelated Emote] Source: " + sourceObj.ToString() + " Target:" + targetObj + " EmoteId: " + emoteId);
            // Currently Unused
        }

        private void OnEmoteOutgoing(IGameObject targetObj, ushort emoteId)
        {
            //Plugin.PluginLog.Info("[OUTGOING EMOTE] Target: " + targetObj.ToString() + " EmoteId: " + emoteId);
            // Currently Unused.
        }

        private void OnEmoteSelf(ushort emoteId)
        {
            //Plugin.PluginLog.Info("[SELF EMOTE] EmoteId: " + emoteId);
            // Currently Unused.
        }


        /* Unused Debug stuff
        private void HandleNetworkMessage(nint dataPtr, ushort OpCode, uint sourceActorId, uint targetActorId, NetworkMessageDirection direction)
        {
            Plugin.PluginLog.Info($"(Net) dataPtr: {dataPtr} - OpCode: {OpCode} - ActorId: {sourceActorId} - TargetId: {targetActorId} - direction: ${direction.ToString()}");

            //if (MasterCharacter != null && MasterCharacter.IsValid() && MasterCharacter.Name + "#" + MasterCharacter.HomeWorld.Id == Plugin.Configuration.MasterNameFull) return;

            var targetOb = Plugin.ObjectTable.FirstOrDefault(x => (ulong)x.GameObjectId == targetActorId);
            if (targetOb != null && targetOb.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
            {
                if (((IPlayerCharacter)targetOb).Name + "#" + ((IPlayerCharacter)targetOb).HomeWorld.Id == Plugin.Configuration.MasterNameFull)
                {
                    MasterCharacter = (IPlayerCharacter)targetOb;
                    Plugin.PluginLog.Info("Found Master Signature!");
                    Plugin.PluginLog.Info(MasterCharacter.ToString());
                    Plugin.GameNetwork.NetworkMessage -= HandleNetworkMessage;
                    return;
                }
                //Plugin.PluginLog.Info(targetOb.ToString());
            }
        }*/

    }
}
