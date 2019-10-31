﻿using System.Threading;
using NonVisuals.Interfaces;

namespace NonVisuals.StreamDeck
{
    public class OSCommandBindingStreamDeck : OSCommandBinding, IStreamDeckButtonAction
    {
        public EnumStreamDeckActionType ActionType => EnumStreamDeckActionType.OSCommand;









        public void Execute(StreamDeckRequisites streamDeckRequisite)
        {
            OSCommandObject.Execute(streamDeckRequisite.ThreadCancellationToken);
        }

        internal override void ImportSettings(string settings) { }

        public override string ExportSettings()
        {
            return null;
        }
    }
}
