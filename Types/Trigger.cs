﻿using System;
using System.Collections.Generic;

namespace WoLightning.Types
{
    [Serializable]
    public class Trigger
    {

        public string Name { get; set; } // Name of the Trigger itself, used for Logging
        public OpType OpMode { get; set; } = OpType.Shock;
        public int Intensity { get; set; } = 1;
        public int Duration { get; set; } = 1;
        public int Cooldown { get; set; } = 0;
        public List<Shocker> Shockers { get; set; } = new(); // List of all Shocker Codes to run on
        public Dictionary<String, int[]>? CustomData { get; set; } // Data that gets generated by the User


        [NonSerialized] public bool isModalOpen = true; // Used for Configwindow

        public Trigger(string Name)
        {
            this.Name = Name;
        }

        public bool Validate()
        {
            return !(Intensity < 1 || Intensity > 100 || Duration < 1 || Duration > 10 || Shockers.Count < 1 || Shockers.Count > 5);
        }
        public bool IsEnabled()
        {
            return Shockers.Count > 0;
        }

        public override string ToString()
        {
            return $"[Trigger] Name:{Name} PiSettings:{OpMode} {Intensity}%|{Duration}s Cooldown:{Cooldown} Applied to {Shockers.Count} Shockers.";
        }

        public string getShockerNames()
        {
            string output = "";
            foreach (var shocker in Shockers) output += shocker.Name + ", ";
            return output;
        }

        public string getShockerNamesNewLine()
        {
            string output = "";
            foreach (var shocker in Shockers) output += shocker.Name + "\n";
            return output;
        }

        public void setupCustomData()
        {
            if(CustomData == null) CustomData = new Dictionary<String, int[]>();
            if (CustomData.Count > 0) return;
            switch (this.Name)
            {
                case "FailMechanic":
                    CustomData.Add("Proportional", [0, 1]);
                    break;
                case "TakeDamage":
                    CustomData.Add("Proportional", [0, 0]);
                    break;
            }
        }

    }
}
