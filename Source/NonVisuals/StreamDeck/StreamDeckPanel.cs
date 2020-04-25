﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using ClassLibraryCommon;
using DCS_BIOS;
using NonVisuals.Interfaces;
using NonVisuals.Saitek;
using NonVisuals.StreamDeck.Events;
using OpenMacroBoard.SDK;
using StreamDeckSharp;

namespace NonVisuals.StreamDeck
{
    public class StreamDeckPanel : GamingPanel, IStreamDeckListener
    {
        private IStreamDeckBoard _streamDeckBoard;
        private int _lcdKnobSensitivity;
        private readonly StreamDeckLayerHandler _streamDeckLayerHandler;
        private readonly object _lcdLockObject = new object();
        private readonly object _lcdDataVariablesLockObject = new object();

        private static readonly List<StreamDeckPanel> StreamDeckPanels = new List<StreamDeckPanel>();

        private long _doUpdatePanelLCD;
        private int _buttonCount = 0;





        public StreamDeckPanel(IStreamDeckListener streamDeckListener, GamingPanelEnum panelType, HIDSkeleton hidSkeleton) : base(GamingPanelEnum.StreamDeck, hidSkeleton)
        {
            switch (panelType)
            {
                case GamingPanelEnum.StreamDeckMini:
                    {
                        _buttonCount = 6;
                        break;
                    }
                case GamingPanelEnum.StreamDeck:
                    {
                        _buttonCount = 15;
                        break;
                    }
                case GamingPanelEnum.StreamDeckXL:
                    {
                        _buttonCount = 32;
                        break;
                    }
            }
            Startup();
            _streamDeckBoard = StreamDeckSharp.StreamDeck.OpenDevice(hidSkeleton.InstanceId, false);
            _streamDeckBoard.KeyStateChanged += StreamDeckKeyListener;
            _streamDeckLayerHandler = new StreamDeckLayerHandler(this);
            _streamDeckLayerHandler.Attach(streamDeckListener);
            StreamDeckPanels.Add(this);
        }

        ~StreamDeckPanel()
        {
            StreamDeckPanels.Remove(this);
        }

        public static StreamDeckPanel GetInstance(string instanceId)
        {
            foreach (var streamDeckPanel in StreamDeckPanels)
            {
                if (streamDeckPanel.InstanceId == instanceId)
                {
                    return streamDeckPanel;
                }
            }

            return null;
        }

        public sealed override void Startup()
        {
            try
            {
                StartListeningForPanelChanges();
            }
            catch (Exception ex)
            {
                Common.DebugP("StreamDeck.StartUp() : " + ex.Message);
                Common.LogError(ex);
            }
        }

        public override void Shutdown()
        {
            try
            {
                Closed = true;
            }
            catch (Exception e)
            {
                SetLastException(e);
            }
        }

        public void AddStreamDeckButtonToActiveLayer(StreamDeckButton streamDeckButton)
        {
            _streamDeckLayerHandler.AddButtonToSelectedLayer(streamDeckButton);
            SetIsDirty();
        }

        private void StreamDeckKeyListener(object sender, KeyEventArgs e)
        {
            if (!(sender is IMacroBoard))
            {
                return;
            }

            if (!ForwardPanelEvent)
            {
                return;
            }
            
            var streamDeckButton = _streamDeckLayerHandler.GetSelectedLayerButton(e.Key + 1);

            if (e.IsDown)
            {
                streamDeckButton.DoPress();
            }
            else
            {
                streamDeckButton.DoRelease(CancellationToken.None);
            }
        }

        protected override void StartListeningForPanelChanges()
        {
            try
            {

            }
            catch (Exception ex)
            {
                Common.DebugP(ex.Message + "\n" + ex.StackTrace);
            }
        }

        public override void DcsBiosDataReceived(object sender, DCSBIOSDataEventArgs e)
        {
            if (SettingsLoading)
            {
                return;
            }
            UpdateCounter(e.Address, e.Data);
        }

        public void SetImage(int streamDeckButtonNumber, Bitmap bitmap)
        {
            SetImage(StreamDeckFunction.ButtonName(streamDeckButtonNumber), bitmap);
        }

        public void SetImage(EnumStreamDeckButtonNames streamDeckButtonName, Bitmap bitmap)
        {
            if (streamDeckButtonName == EnumStreamDeckButtonNames.BUTTON0_NO_BUTTON)
            {
                return;
            }
            var keyBitmap = KeyBitmap.Create.FromBitmap(bitmap);
            _streamDeckBoard.SetKeyBitmap(StreamDeckFunction.ButtonNumber(streamDeckButtonName) - 1, keyBitmap);
        }

        public void ClearAllFaces()
        {
            _streamDeckLayerHandler.ClearAllFaces();
        }

        public override List<string> ExportSettings()
        {
            if (Closed)
            {
                return null;
            }
            return new List<string>();
        }

        public override void ImportSettings(List<string> settings)
        {
            SettingsLoading = true;
            //Clear current bindings
            ClearSettings();
            if (settings == null || settings.Count == 0)
            {
                return;
            }

            var stringBuilder = new StringBuilder();

            foreach (var setting in settings)
            {
                if (!setting.StartsWith("#") && setting.Length > 2 && setting.Contains(InstanceId))
                {
                    stringBuilder.Append(setting.Replace(SaitekConstants.SEPARATOR_SYMBOL, "").Replace(InstanceId, "") + Environment.NewLine);
                }
            }
            _streamDeckLayerHandler.ImportJSONSettings(stringBuilder.ToString());
            SettingsLoading = false;
            SettingsApplied();
        }

        private string ExportJSONSettings()
        {
            if (Closed)
            {
                return null;
            }

            return _streamDeckLayerHandler.ExportJSONSettings();
        }

        public string GetKeyPressForLoggingPurposes(StreamDeckButton streamDeckButton)
        {
            var result = "";
            /*foreach (var keyBinding in _keyBindings)
            {
                if (keyBinding.OSKeyPress != null && keyBinding.EnumStreamDeckButtonName == enumStreamDeckButton.EnumStreamDeckButtonName && keyBinding.WhenTurnedOn == enumStreamDeckButton.IsPressed)
                {
                    result = keyBinding.OSKeyPress.GetNonFunctioningVirtualKeyCodesAsString();
                }
            }*/
            return result;
        }

        public override void SavePanelSettings(object sender, ProfileHandlerEventArgs e) { }

        public override void SavePanelSettingsJSON(object sender, ProfileHandlerEventArgs e)
        {
            e.ProfileHandlerEA.RegisterJSONProfileData(this, ExportJSONSettings());
        }

        public override void ClearSettings()
        {
            /*_keyBindings.Clear();
            _osCommandBindings.Clear();
            _dcsBiosBindings.Clear();
            _dcsBiosLcdBindings.Clear();
            _bipLinks.Clear();*/
            _streamDeckLayerHandler.ClearSettings();
        }

        protected override void GamingPanelKnobChanged(IEnumerable<object> hashSet)
        {
            //Set _selectedMode and LCD button statuses
            //and performs the actual actions for key presses
            // ADD METHOD ?
        }


        private void StreamDeckButtonChanged(IEnumerable<object> hashSet)
        {
            if (!ForwardPanelEvent)
            {
                return;
            }
        }

        private void DeviceAttachedHandler()
        {
            Startup();
            //IsAttached = true;
        }

        private void DeviceRemovedHandler()
        {
            Shutdown();
            //IsAttached = false;
        }

        public DcsOutputAndColorBinding CreateDcsOutputAndColorBinding(SaitekPanelLEDPosition saitekPanelLEDPosition, PanelLEDColor panelLEDColor, DCSBIOSOutput dcsBiosOutput)
        {
            return null;
        }

        public int LCDKnobSensitivity
        {
            get => _lcdKnobSensitivity;
            set => _lcdKnobSensitivity = value;
        }


        public string SelectedLayerName
        {
            get => _streamDeckLayerHandler.SelectedLayerName;
            set => _streamDeckLayerHandler.SelectedLayerName = value;
        }

        public override string SettingsVersion()
        {
            return "2X";
        }

        public List<string> LayerNameList
        {
            get => _streamDeckLayerHandler.GetLayerNameList();
        }

        public List<StreamDeckLayer> LayerList
        {
            get => _streamDeckLayerHandler.LayerList;
        }

        public bool AddLayer(StreamDeckLayer streamDeckLayer)
        {
            SetIsDirty();
            return _streamDeckLayerHandler.AddLayer(streamDeckLayer);
        }

        public void DeleteLayer(string streamDeckLayerName)
        {
            _streamDeckLayerHandler.DeleteLayer(streamDeckLayerName);
            SetIsDirty();
        }

        public StreamDeckLayer HomeLayer
        {
            get => _streamDeckLayerHandler.HomeLayer;
        }

        public List<string> GetStreamDeckLayerNames()
        {
            return _streamDeckLayerHandler.GetStreamDeckLayerNames();
        }

        public StreamDeckButton GetStreamDeckButton(EnumStreamDeckButtonNames streamDeckButtonName, string layerName)
        {
            return _streamDeckLayerHandler.GetButton(streamDeckButtonName, layerName);
        }

        public StreamDeckButton GetCurrentLayerButton(EnumStreamDeckButtonNames streamDeckButtonName)
        {
            return _streamDeckLayerHandler.GetSelectedLayerButton(streamDeckButtonName);
        }

        public StreamDeckLayer GetLayer(string layerName)
        {
            return _streamDeckLayerHandler.GetLayer(layerName);
        }

        public StreamDeckLayer SelectedLayer
        {
            get => _streamDeckLayerHandler.SelectedLayer;
            set => _streamDeckLayerHandler.SelectedLayer = value;
        }

        public bool HasLayers
        {
            get { return _streamDeckLayerHandler.HasLayers; }
        }

        public void ShowHomeLayer()
        {
            _streamDeckLayerHandler.ShowHomeLayer();
        }

        public void ShowPreviousLayer()
        {
            _streamDeckLayerHandler.ShowPreviousLayer();
        }

        public IStreamDeckBoard StreamDeckBoard => _streamDeckBoard;

        public int ButtonCount => _buttonCount;

        public int SelectedButtonNumber
        {
            get => _streamDeckLayerHandler.SelectedButtonNumber;
            set => _streamDeckLayerHandler.SelectedButtonNumber = value;
        }

        public EnumStreamDeckButtonNames SelectedButtonName
        {
            get => _streamDeckLayerHandler.SelectedButtonName;
            set => _streamDeckLayerHandler.SelectedButtonName = value;
        }

        public StreamDeckButton SelectedButton
        {
            get => SelectedLayer.GetStreamDeckButton(SelectedButtonName);
        }

        public static Bitmap Validate(string imagePath)
        {
            if (File.Exists(imagePath))
            {
                return new Bitmap(imagePath);
            }

            var uri = new Uri("pack://application:,,,/NonVisuals;component/Images/filenotfound.png");
            return new Bitmap(uri.AbsolutePath);
        }

        public void RemoveButton(StreamDeckButton streamDeckButton)
        {
            _streamDeckLayerHandler.RemoveButton(streamDeckButton);
            SetIsDirty();
        }

        public void Attach(IStreamDeckListener streamDeckListener)
        {
            _streamDeckLayerHandler.Attach(streamDeckListener);
        }

        public void Detach(IStreamDeckListener streamDeckListener)
        {
            _streamDeckLayerHandler.Detach(streamDeckListener);
        }

        public void LayerSwitched(object sender, StreamDeckLayerSwitchArgs e)
        {
            try
            {
                ClearAllFaces();
            }
            catch (Exception ex)
            {
                Common.LogError(ex);
            }
        }

        public void SelectedButtonChanged(object sender, StreamDeckSelectedButtonChangeArgs e)
        {
            try
            {
            }
            catch (Exception ex)
            {
                Common.LogError(ex);
            }
        }

        public void SelectedButtonChangePreview(object sender, StreamDeckSelectedButtonChangePreviewArgs e)
        {
            try
            {
            }
            catch (Exception ex)
            {
                Common.LogError(ex);
            }
        }
    }

}
