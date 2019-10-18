﻿namespace NonVisuals.Saitek
{
    public abstract class KeyBinding
    {
        /*
         This is the base class for all the key binding classes
         that binds a physical switch to a user made virtual 
         keypress in Windows or other functionality.
         */
        private KeyPress _osKeyPress;
        private bool _whenOnTurnedOn = true;
        protected const string SeparatorChars = "\\o/";

        internal abstract void ImportSettings(string settings);

        public KeyPress OSKeyPress
        {
            get => _osKeyPress;
            set => _osKeyPress = value;
        }
        
        public abstract string ExportSettings();

        public bool WhenTurnedOn
        {
            get => _whenOnTurnedOn;
            set => _whenOnTurnedOn = value;
        }

    }
}
