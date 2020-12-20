﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using ClassLibraryCommon;
using DCS_BIOS;
using NonVisuals.Interfaces;
using NonVisuals.Properties;
using Theraot.Core;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace NonVisuals
{

    public class ProfileHandler : IProfileHandlerListener, IIsDirty
    {
        //Both directory and filename
        private string _filename = Path.GetFullPath((Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))) + "\\" + "dcsfp_profile.bindings";
        private string _lastProfileUsed = "";
        private bool _isDirty;
        private bool _isNewProfile;
        //private readonly List<string> _listPanelSettingsData = new List<string>();
        private readonly object _lockObject = new object();
        private const string OPEN_FILE_DIALOG_FILE_NAME = "*.bindings";
        private const string OPEN_FILE_DIALOG_DEFAULT_EXT = ".bindings";
        private const string OPEN_FILE_DIALOG_FILTER = "DCSFlightpanels (.bindings)|*.bindings";
        //private DCSFPProfile _airframe = DCSFPProfile.NOFRAMELOADEDYET;
        private DCSFPProfile _dcsfpProfile = DCSFPProfile.GetNoFrameLoadedYet();

        private readonly List<KeyValuePair<string, GamingPanelEnum>> _profileFileInstanceIDs = new List<KeyValuePair<string, GamingPanelEnum>>();
        private bool _profileLoaded;

        private IHardwareConflictResolver _hardwareConflictResolver;








        public ProfileHandler(string dcsbiosJSONDirectory)
        {
            DCSBIOSControlLocator.JSONDirectory = dcsbiosJSONDirectory;
        }

        public ProfileHandler(string dcsbiosJSONDirectory, string lastProfileUsed)
        {
            DCSBIOSControlLocator.JSONDirectory = dcsbiosJSONDirectory;
            _lastProfileUsed = lastProfileUsed;
        }

        public string OpenProfile()
        {
            if (IsDirty && MessageBox.Show("Discard unsaved changes to current profile?", "Discard changes?", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
            {
                return null;
            }

            var tempDirectory = string.IsNullOrEmpty(Settings.Default.LastImageFileDialogLocation) ? Constants.PathRootDriveC : Settings.Default.LastImageFileDialogLocation;
            ClearAll();
            var openFileDialog = new OpenFileDialog();
            openFileDialog.RestoreDirectory = true;
            openFileDialog.InitialDirectory = tempDirectory;
            openFileDialog.FileName = OPEN_FILE_DIALOG_FILE_NAME;
            openFileDialog.DefaultExt = OPEN_FILE_DIALOG_DEFAULT_EXT;
            openFileDialog.Filter = OPEN_FILE_DIALOG_FILTER;
            if (openFileDialog.ShowDialog() == true)
            {
                Settings.Default.LastImageFileDialogLocation = Path.GetDirectoryName(openFileDialog.FileName);
                Settings.Default.Save();
                return openFileDialog.FileName;
            }

            return null;
        }

        public void PanelBindingReadFromFile(object sender, PanelBindingReadFromFileEventArgs e) { }

        public void PanelSettingsChanged(object sender, PanelEventArgs e)
        {
            try
            {
                IsDirty = true;
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        public void NewProfile()
        {
            if (IsDirty && MessageBox.Show("Discard unsaved changes to current profile?", "Discard changes?", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
            {
                return;
            }
            _isNewProfile = true;
            ClearAll();
            Profile = DCSFPProfile.GetNoFrameLoadedYet();//Just a default that doesn't remove non emulation panels from the GUI
            Common.UseGenericRadio = false;
            //This sends info to all to clear their settings
            OnClearPanelSettings?.Invoke(this);
        }

        public void ClearAll()
        {
            _profileFileInstanceIDs.Clear();
        }

        /*public string DefaultFile()
        {
            return Path.GetFullPath((Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))) + "\\" + "dcsfp_profile.bindings";
        }*/
        
        public bool ReloadProfile()
        {
            return LoadProfile(null, _hardwareConflictResolver);
        }

        public bool LoadProfile(string filename, IHardwareConflictResolver hardwareConflictResolver)
        {
            try
            {
                /*
                 * 0 Open specified filename (parameter) if not null
                 * 1 If exists open last profile used (settings)
                 * 2 Try and open default profile located in My Documents
                 * 3 If none found create default file
                 */
                _hardwareConflictResolver = hardwareConflictResolver;

                _isNewProfile = false;
                ClearAll();

                if (!string.IsNullOrEmpty(filename))
                {
                    _filename = filename;
                    _lastProfileUsed = filename;
                }
                else
                {
                    if (!string.IsNullOrEmpty(_lastProfileUsed) && File.Exists(_lastProfileUsed))
                    {
                        _filename = _lastProfileUsed;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (string.IsNullOrEmpty(_filename) || !File.Exists(_filename))
                {
                    //Main window will handle this
                    return false;
                }
                /*
                 * Read all information and add InstanceID to all lines using BeginPanel and EndPanel
                 *             
                 * PanelType=PZ55SwitchPanel
                 * PanelInstanceID=\\?\hid#vid_06a3&pid_0d06#8&3f11a32&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}
                 * BeginPanel
                 *      SwitchPanelKey{1KNOB_ENGINE_RIGHT}\o/OSKeyPress{FiftyMilliSec,LSHIFT + VK_Q}
                 *      SwitchPanelKey{1KNOB_ENGINE_LEFT}\o/OSKeyPress{FiftyMilliSec,LCONTROL + VK_Q}
                 *      SwitchPanelKey{1KNOB_ENGINE_BOTH}\o/OSKeyPress{FiftyMilliSec,LSHIFT + VK_C}
                 * EndPanel
                 * 
                 */
                _profileLoaded = true;
                Debug.WriteLine("ProfileHandler reading file ".Append(_filename));
                var fileLines = File.ReadAllLines(_filename);
                var currentPanelType = GamingPanelEnum.Unknown;
                string currentPanelInstanceID = null;
                string currentBindingHash = null;
                var insidePanel = false;
                var insideJSONPanel = false;

                GenericPanelBinding genericPanelBinding = null;

                foreach (var fileLine in fileLines)
                {
                    if (fileLine.StartsWith("Airframe=")) // <== Backward compability
                    {
                        if (fileLine.StartsWith("Airframe=NONE"))
                        {
                            //Backward compability
                            Profile = DCSFPProfile.GetKeyEmulator();
                        }
                        else
                        {
                            //Backward compability
                            var airframeAsString = fileLine.Replace("Airframe=", "").Trim();
                            Profile = DCSFPProfile.GetBackwardCompatible(airframeAsString);
                        }
                    }
                    else if (fileLine.StartsWith("Profile="))
                    {
                        Profile = DCSFPProfile.GetProfile(int.Parse(fileLine.Replace("Profile=", "")));
                    }
                    else if (fileLine.StartsWith("OperationLevelFlag="))
                    {
                        Common.SetOperationModeFlag(int.Parse(fileLine.Replace("OperationLevelFlag=", "").Trim()));
                    }
                    else if (fileLine.StartsWith("UseGenericRadio="))
                    {
                        Common.UseGenericRadio = (bool.Parse(fileLine.Replace("UseGenericRadio=", "").Trim()));
                    }
                    else if (!fileLine.StartsWith("#") && fileLine.Length > 0)
                    {
                        //Process all these lines.
                        if (fileLine.StartsWith("PanelType="))
                        {
                            currentPanelType = (GamingPanelEnum)Enum.Parse(typeof(GamingPanelEnum), fileLine.Replace("PanelType=", "").Trim());
                            genericPanelBinding = new GenericPanelBinding();
                            genericPanelBinding.PanelType = currentPanelType;
                        }
                        else if (fileLine.StartsWith("PanelInstanceID="))
                        {
                            currentPanelInstanceID = fileLine.Replace("PanelInstanceID=", "").Trim();
                            genericPanelBinding.HIDInstance = currentPanelInstanceID;
                            _profileFileInstanceIDs.Add(new KeyValuePair<string, GamingPanelEnum>(currentPanelInstanceID, currentPanelType));
                        }
                        else if (fileLine.StartsWith("BindingHash="))
                        {
                            currentBindingHash = fileLine.Replace("BindingHash=", "").Trim();
                            genericPanelBinding.BindingHash = currentBindingHash;
                        }
                        else if (fileLine.StartsWith("PanelSettingsVersion="))
                        {
                            //do nothing, this will be phased out
                        }
                        else if (fileLine.Equals("BeginPanel"))
                        {
                            insidePanel = true;
                        }
                        else if (fileLine.Equals("EndPanel"))
                        {
                            if (genericPanelBinding != null)
                            {
                                BindingMappingManager.RegisterBindingFromFile(genericPanelBinding);
                            }
                            insidePanel = false;
                        }
                        else if (fileLine.Equals("BeginPanelJSON"))
                        {
                            insideJSONPanel = true;
                        }
                        else if (fileLine.Equals("EndPanelJSON"))
                        {
                            if (genericPanelBinding != null)
                            {
                                BindingMappingManager.RegisterBindingFromFile(genericPanelBinding);
                            }
                            insideJSONPanel = false;
                        }
                        else
                        {
                            if (insidePanel)
                            {
                                var line = fileLine;
                                if (line.StartsWith("\t"))
                                {
                                    line = line.Replace("\t", "");
                                }
                                
                                genericPanelBinding.Settings.Add(line);
                            }

                            if (insideJSONPanel)
                            {
                                genericPanelBinding.JSONAddLine(fileLine);
                            }
                        }
                    }
                }
                //For backwards compability 10.11.2018
                if (Common.GetOperationModeFlag() == 0)
                {
                    SetOperationLevelFlag();
                }

                CheckHardwareConflicts();

                SendBindingsReadEvent();
                return true;
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
                return false;
            }
        }

        private void CheckHardwareConflicts()
        {
            var settingsWereModified = false;
            if (!BindingMappingManager.VerifyBindings(ref settingsWereModified))
            {
                var modifiedBindings = _hardwareConflictResolver.ResolveConflicts();
                BindingMappingManager.MergeModifiedBindings(modifiedBindings);
                settingsWereModified = modifiedBindings != null && modifiedBindings.Count > 0;
            }
            if (settingsWereModified)
            {
                SetIsDirty();
            }
        }

        private void SetOperationLevelFlag()
        {
            if (DCSFPProfile.IsKeyEmulator(Profile))
            {
                Common.SetEmulationMode(EmulationMode.KeyboardEmulationOnly);
            }
            else if (DCSFPProfile.IsKeyEmulatorSRS(Profile))
            {
                Common.SetEmulationMode(EmulationMode.KeyboardEmulationOnly);
                Common.SetEmulationMode(EmulationMode.SRSEnabled);
            }
            else if (DCSFPProfile.IsFlamingCliff(Profile))
            {
                Common.SetEmulationMode(EmulationMode.SRSEnabled);
                Common.SetEmulationMode(EmulationMode.DCSBIOSOutputEnabled);
            }
            else
            {
                Common.SetEmulationMode(EmulationMode.DCSBIOSOutputEnabled | EmulationMode.DCSBIOSInputEnabled);
            }
        }

        public void SendBindingsReadEvent()
        {
            try
            {
                if (OnSettingsReadFromFile != null)
                {
                    OnAirframeSelected?.Invoke(this, new AirframeEventArgs() { Profile = _dcsfpProfile });

                    foreach (var genericPanelBinding in BindingMappingManager.PanelBindings)
                    {
                        try
                        {
                            OnSettingsReadFromFile(this, new PanelBindingReadFromFileEventArgs() { PanelBinding = genericPanelBinding });
                        }
                        catch (Exception e)
                        {
                            Common.ShowErrorMessageBox(e, "Error reading settings. Panel : " + genericPanelBinding.PanelType);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Common.ShowErrorMessageBox(e);
            }
        }

        public void SendRadioSettings()
        {
            try
            {
                if (OnSettingsReadFromFile != null)
                {
                    foreach (var genericPanelBinding in BindingMappingManager.PanelBindings)
                    {
                        try
                        {
                            if (genericPanelBinding.PanelType == GamingPanelEnum.PZ69RadioPanel)
                            {
                                OnSettingsReadFromFile(this, new PanelBindingReadFromFileEventArgs() { PanelBinding = genericPanelBinding });
                            }
                        }
                        catch (Exception e)
                        {
                            Common.ShowErrorMessageBox(e, "Error reading settings. Panel : " + genericPanelBinding.PanelType);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Common.ShowErrorMessageBox(e);
            }
        }

        public bool SaveAsNewProfile()
        {
            var saveFileDialog = new SaveFileDialog();
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.InitialDirectory = string.IsNullOrEmpty(Settings.Default.LastImageFileDialogLocation) ? Constants.PathRootDriveC : Settings.Default.LastImageFileDialogLocation;
            saveFileDialog.FileName = "dcsfp_profile.bindings";
            saveFileDialog.DefaultExt = OPEN_FILE_DIALOG_DEFAULT_EXT;
            saveFileDialog.Filter = OPEN_FILE_DIALOG_FILTER;
            saveFileDialog.OverwritePrompt = true;
            if (saveFileDialog.ShowDialog() == true)
            {
                _isNewProfile = false;
                Filename = saveFileDialog.FileName;
                _lastProfileUsed = Filename;
                SaveProfile();
                return true;
            }
            return false;
        }

        public void SendEventRegardingSavingPanelConfigurations()
        {
            OnSavePanelSettings?.Invoke(this, new ProfileHandlerEventArgs() { ProfileHandlerEA = this });
            OnSavePanelSettingsJSON?.Invoke(this, new ProfileHandlerEventArgs() { ProfileHandlerEA = this });
        }

        public bool IsNewProfile => _isNewProfile;

        public string Filename
        {
            get => _filename;
            set => _filename = value;
        }

        public void RegisterPanelBinding(GamingPanel gamingPanel, List<string> strings)
        {
            try
            {
                lock (_lockObject)
                {
                    var genericPanelBinding = BindingMappingManager.GetBinding(gamingPanel);

                    if (genericPanelBinding == null)
                    {
                        genericPanelBinding = new GenericPanelBinding(gamingPanel.HIDInstanceId, gamingPanel.BindingHash, gamingPanel.TypeOfPanel);
                        BindingMappingManager.AddBinding(genericPanelBinding);
                    }

                    genericPanelBinding.ClearSettings();

                    foreach (var str in strings)
                    {
                        genericPanelBinding.Settings.Add(str);
                    }
                }
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        public void RegisterJSONProfileData(GamingPanel gamingPanel, string jsonData)
        {
            try
            {
                lock (_lockObject)
                {
                    var genericPanelBinding = BindingMappingManager.GetBinding(gamingPanel);

                    if (genericPanelBinding == null)
                    {
                        genericPanelBinding = new GenericPanelBinding(gamingPanel.HIDInstanceId, gamingPanel.BindingHash, gamingPanel.TypeOfPanel);
                        BindingMappingManager.AddBinding(genericPanelBinding);
                    }

                    genericPanelBinding.JSONString = jsonData;
                }
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        public void SaveProfile()
        {
            try
            {
                //Clear all current settings entries, requesting new ones from the panels
                ClearAll();
                SendEventRegardingSavingPanelConfigurations();
                if (IsNewProfile)
                {
                    SaveAsNewProfile();
                    return;
                }
                _lastProfileUsed = Filename;

                var headerStringBuilder = new StringBuilder();
                headerStringBuilder.Append("#This file can be manually edited using any ASCII editor.\n#File created on " + DateTime.Today + " " + DateTime.Now);
                headerStringBuilder.AppendLine("#");
                headerStringBuilder.AppendLine("#");
                headerStringBuilder.AppendLine("#IMPORTANT INFO REGARDING the keyboard key AltGr (RAlt as named in DCS) or RMENU as named DCSFP");
                headerStringBuilder.AppendLine("#When you press AltGr DCSFP will register RMENU + LCONTROL. This is a bug which \"just is\". You need to modify that in the profile");
                headerStringBuilder.AppendLine("#by deleting the + LCONTROL part.");
                headerStringBuilder.AppendLine("#So for example AltGr + HOME pressed on the keyboard becomes RMENU + LCONTROL + HOME");
                headerStringBuilder.AppendLine("#Open text editor and delete the LCONTROL ==> RMENU + HOME");
                headerStringBuilder.AppendLine("#Supported panels :");
                headerStringBuilder.AppendLine("#   PZ55SwitchPanel");
                headerStringBuilder.AppendLine("#   PZ69RadioPanel");
                headerStringBuilder.AppendLine("#   PZ70MultiPanel");
                headerStringBuilder.AppendLine("#   BackLitPanel");
                headerStringBuilder.AppendLine("#   StreamDeckMini");
                headerStringBuilder.AppendLine("#   StreamDeck");
                headerStringBuilder.AppendLine("#   StreamDeckXL");

                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(headerStringBuilder.ToString());
                stringBuilder.AppendLine("#  ***Do not change the location nor content of the line below***");
                stringBuilder.AppendLine("Profile=" + Profile.ID);
                stringBuilder.AppendLine("OperationLevelFlag=" + Common.GetOperationModeFlag());
                stringBuilder.AppendLine("UseGenericRadio=" + Common.UseGenericRadio + Environment.NewLine);

                foreach (var genericPanelBinding in BindingMappingManager.PanelBindings)
                {
                    if (!genericPanelBinding.HasBeenDeleted)
                    {
                        stringBuilder.AppendLine(genericPanelBinding.ExportBinding());
                    }
                }

                stringBuilder.AppendLine(GetFooter());

                File.WriteAllText(_filename, stringBuilder.ToString(), Encoding.ASCII);
                _isDirty = false;
                _isNewProfile = false;
                LoadProfile(_filename, _hardwareConflictResolver);
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        private string GetFooter()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(Environment.NewLine + Environment.NewLine + Environment.NewLine + Environment.NewLine + "#--------------------------------------------------------------------");
            stringBuilder.AppendLine("#Below are all the Virtual Keycodes in use listed. You can manually edit this file using these codes.");
            var enums = Enum.GetNames(typeof(VirtualKeyCode));
            foreach (var @enum in enums)
            {
                stringBuilder.AppendLine("#\t" + @enum);
            }
            return stringBuilder.ToString();
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (value)
                {
                    var asd = 1;
                }
                _isDirty = value;
            }
        }

        public void SetIsDirty()
        {
            IsDirty = true;
        }

        /*public DCSFPProfile Airframe
        {
            get => _airframe;
            set
            {
                //Called only when user creates a new profile
                if (value != _airframe)
                {
                    SetIsDirty();
                }
                _airframe = value;
                Common.ResetOperationModeFlag();
                SetOperationLevelFlag();
                OnAirframeSelected?.Invoke(this, new AirframeEventArgs() { Airframe = _airframe });
            }
        }*/

        public DCSFPProfile Profile
        {
            get => _dcsfpProfile;
            set
            {
                //Called only when user creates a new profile
                if (!DCSFPProfile.IsNoFrameLoadedYet(_dcsfpProfile) && value != _dcsfpProfile)
                {
                    SetIsDirty();
                }
                _dcsfpProfile = value;
                Common.ResetOperationModeFlag();
                SetOperationLevelFlag();
                DCSBIOSControlLocator.Profile = Profile;
                OnAirframeSelected?.Invoke(this, new AirframeEventArgs() { Profile = _dcsfpProfile });
            }
        }

        public string LastProfileUsed
        {
            get => _lastProfileUsed;
            set => _lastProfileUsed = value;
        }

        public string DCSBIOSJSONDirectory
        {
            get => DCSBIOSControlLocator.JSONDirectory;
            set => DCSBIOSControlLocator.JSONDirectory = value;
        }

        public void SelectedProfile(object sender, AirframeEventArgs e) { }

        public bool ProfileLoaded => _profileLoaded || _isNewProfile;

        public bool UseNS430
        {
            get => Common.IsOperationModeFlagSet(EmulationMode.NS430Enabled);
            set
            {
                if (value)
                {
                    Common.SetEmulationMode(EmulationMode.NS430Enabled);
                }
                else
                {
                    Common.ClearOperationModeFlag(EmulationMode.NS430Enabled);
                }
                SetIsDirty();
            }
        }


        public delegate void ProfileReadFromFileEventHandler(object sender, PanelBindingReadFromFileEventArgs e);
        public event ProfileReadFromFileEventHandler OnSettingsReadFromFile;

        public delegate void SavePanelSettingsEventHandler(object sender, ProfileHandlerEventArgs e);
        public event SavePanelSettingsEventHandler OnSavePanelSettings;

        public delegate void SavePanelSettingsEventHandlerJSON(object sender, ProfileHandlerEventArgs e);
        public event SavePanelSettingsEventHandlerJSON OnSavePanelSettingsJSON;

        public delegate void AirframeSelectedEventHandler(object sender, AirframeEventArgs e);
        public event AirframeSelectedEventHandler OnAirframeSelected;

        public delegate void ClearPanelSettingsEventHandler(object sender);
        public event ClearPanelSettingsEventHandler OnClearPanelSettings;

        public delegate void UserMessageEventHandler(object sender, UserMessageEventArgs e);
        public event UserMessageEventHandler OnUserMessageEventHandler;

        public void Attach(GamingPanel gamingPanel)
        {
            OnSettingsReadFromFile += gamingPanel.PanelBindingReadFromFile;
            OnSavePanelSettings += gamingPanel.SavePanelSettings;
            OnSavePanelSettingsJSON += gamingPanel.SavePanelSettingsJSON;
            OnClearPanelSettings += gamingPanel.ClearPanelSettings;
            OnAirframeSelected += gamingPanel.SelectedProfile;
        }

        public void Detach(GamingPanel gamingPanel)
        {
            OnSettingsReadFromFile -= gamingPanel.PanelBindingReadFromFile;
            OnSavePanelSettings -= gamingPanel.SavePanelSettings;
            OnSavePanelSettingsJSON -= gamingPanel.SavePanelSettingsJSON;
            OnClearPanelSettings -= gamingPanel.ClearPanelSettings;
            OnAirframeSelected -= gamingPanel.SelectedProfile;
        }

        public void Attach(IProfileHandlerListener gamingPanelSettingsListener)
        {
            OnSettingsReadFromFile += gamingPanelSettingsListener.PanelBindingReadFromFile;
            OnAirframeSelected += gamingPanelSettingsListener.SelectedProfile;
        }

        public void Detach(IProfileHandlerListener gamingPanelSettingsListener)
        {
            OnSettingsReadFromFile -= gamingPanelSettingsListener.PanelBindingReadFromFile;
            OnAirframeSelected -= gamingPanelSettingsListener.SelectedProfile;
        }

        public void AttachUserMessageHandler(IUserMessageHandler userMessageHandler)
        {
            OnUserMessageEventHandler += userMessageHandler.UserMessage;
        }

        public void DetachUserMessageHandler(IUserMessageHandler userMessageHandler)
        {
            OnUserMessageEventHandler -= userMessageHandler.UserMessage;
        }

        public void StateSaved()
        {

        }
    }

    public class AirframeEventArgs : EventArgs
    {
        public DCSFPProfile Profile { get; set; }
    }

    public class ProfileHandlerEventArgs : EventArgs
    {
        public ProfileHandler ProfileHandlerEA { get; set; }
    }

    public class UserMessageEventArgs : EventArgs
    {
        public string UserMessage { get; set; }
    }



}
