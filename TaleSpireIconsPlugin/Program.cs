using BepInEx;
using UnityEngine;
using UnityEngine.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using BepInEx.Configuration;
using System.Text.RegularExpressions;
using System.Collections;

namespace LordAshes
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(LordAshes.AssetDataPlugin.Guid)]
    [BepInDependency(RadialUI.RadialUIPlugin.Guid)]
    [BepInDependency(LordAshes.FileAccessPlugin.Guid)]
    public partial class IconsPlugin : BaseUnityPlugin
    {
        // Plugin info
        public const string Name = "Icons Plug-In";
        public const string Guid = "org.lordashes.plugins.icons";
        public const string Version = "2.1.1.0";

        // Plublic enum
        public enum DiagnostiocLevel
        {
            none = 0,
            low = 1,
            medium = 2,
            high = 3,
            ultra = 4
        }

        // Configuration
        private ConfigEntry<KeyboardShortcut> triggerIconsMenu { get; set; }
        private ConfigEntry<bool> performanceHigh { get; set; }
        private ConfigEntry<float> delayIconProcessing { get; set; }
        private ConfigEntry<DiagnostiocLevel> diagnosticLevel { get; set; }

        // Data Change Handlers
        private List<CreatureGuid> iconifiedAssets = new List<CreatureGuid>();

        // Subscription
        private static BoardState boardReady = BoardState.boardNotReady;
        private List<AssetDataPlugin.DatumChange> backlog = new List<AssetDataPlugin.DatumChange>();

        // Variables used by the synchronization function. Exposed here so that the function does not constantly allocate and deallocate memory
        GameObject icon0;
        GameObject icon1;
        GameObject icon2;
        Vector3 offset0;
        Vector3 offset1;
        Vector3 offset2;

        public enum BoardState
        {
            boardNotReady = 0,
            boardIconsBuildDelay = 1,
            boardReady = 2
        }

        /// <summary>
        /// Function for initializing plugin
        /// This function is called once by TaleSpire
        /// </summary>
        void Awake()
        {
            diagnosticLevel = Config.Bind("Setting", "Diagnostic Details In Log", DiagnostiocLevel.low);

            UnityEngine.Debug.Log("Icons Plugin: "+GetType().AssemblyQualifiedName+" is Active. (Diagnostic Level: "+diagnosticLevel.Value.ToString()+")");

            triggerIconsMenu = Config.Bind("Hotkeys", "Icons Toggle Menu", new KeyboardShortcut(KeyCode.I, KeyCode.LeftControl));
            performanceHigh = Config.Bind("Settings", "Use High Performance (uses higher CPU load)", true);
            delayIconProcessing = Config.Bind("Setting", "Delay Icon Processing On Startup", 3.0f);

            // Add Info menu selection to main character menu
            RadialUI.RadialSubmenu.EnsureMainMenuItem(  RadialUI.RadialUIPlugin.Guid + ".Icons",
                                                        RadialUI.RadialSubmenu.MenuType.character,
                                                        "Icons",
                                                        FileAccessPlugin.Image.LoadSprite("Icons/Icons.png")
                                                     );

            // Add Icons sub menu item
            Regex regex = new Regex("Icons/" + IconsPlugin.Guid + @"/(.+)\.(P|p)(N|n)(G|g)$");
            foreach (String iconFile in FileAccessPlugin.File.Catalog())
            {
                if (diagnosticLevel.Value >= DiagnostiocLevel.ultra) { Debug.Log("Icons Plugin: Comparing '" + iconFile + "' To Regex"); }
                if (regex.IsMatch(iconFile))
                {
                    if (diagnosticLevel.Value >= DiagnostiocLevel.high) { Debug.Log("Icons Plugin: Found Icons '" + iconFile + "'"); } 
                    RadialUI.RadialSubmenu.CreateSubMenuItem(RadialUI.RadialUIPlugin.Guid + ".Icons",
                                                                System.IO.Path.GetFileNameWithoutExtension(iconFile),
                                                                FileAccessPlugin.Image.LoadSprite(iconFile),
                                                                (a,b,c)=> { ToggleIcon(a, iconFile); },
                                                                true,
                                                                () => 
                                                                {
                                                                    if (diagnosticLevel.Value >= DiagnostiocLevel.medium) Debug.Log("Icons Plugin: Adding Icon '"+iconFile+"'");  
                                                                    return LocalClient.HasControlOfCreature(new CreatureGuid(RadialUI.RadialUIPlugin.GetLastRadialTargetCreature())); 
                                                                }
                                                            );
                }
            }

            AssetDataPlugin.Subscribe(IconsPlugin.Guid, (change)=> { HandleRequest( new AssetDataPlugin.DatumChange[] { change }); });

            // Display plugin on the main TaleSpire page
            Utility.PostOnMainPage(this.GetType());
        }

        /// <summary>
        /// Function for determining if view mode has been toggled and, if so, activating or deactivating Character View mode.
        /// This function is called periodically by TaleSpire.
        /// </summary>
        void Update()
        {
            if (Utility.isBoardLoaded())
            {
                if (boardReady == BoardState.boardNotReady)
                {
                    boardReady = BoardState.boardIconsBuildDelay;
                    if (diagnosticLevel.Value >= DiagnostiocLevel.high) { Debug.Log("Icons Plugin: Board Loaded Delaying Message Processing To Allow Minis To Load"); }
                    StartCoroutine("DelayIconProcessing", new object[] { delayIconProcessing.Value });
                }

                // Check for keyboard triggered menu
                if (Utility.StrictKeyCheck(triggerIconsMenu.Value))
                {
                    ShowIconMenu(LocalClient.SelectedCreatureId);
                }

                // Sync icons for selected mini
                if (!performanceHigh.Value)
                {
                    // Low Performance Mode - Sync Only Selected Mini
                    if (LocalClient.SelectedCreatureId != null)
                    {
                        if (iconifiedAssets.Contains(LocalClient.SelectedCreatureId))
                        {
                            SyncBaseWithIcons(LocalClient.SelectedCreatureId);
                        }
                    }
                }
                else
                {
                    // High Performance Mode - Sync All Iconified Minis
                    foreach (CreatureGuid mini in iconifiedAssets)
                    {
                        SyncBaseWithIcons(mini);
                    }
                }
            }
            else
            {
                if (boardReady != BoardState.boardNotReady)
                {
                    if (diagnosticLevel.Value >= DiagnostiocLevel.high) { Debug.Log("Icons Plugin: Board Unloaded"); }
                    boardReady = BoardState.boardNotReady;
                }
            }
        }

        /// <summary>
        /// Method to generate GUI menu
        /// </summary>
        public void ShowIconMenu(CreatureGuid cid)
        {
            if (diagnosticLevel.Value >= DiagnostiocLevel.medium) { Debug.Log("Icons Plugin: Showing WinForms Based Menu"); }
            Form menu = new Form();
            menu.SuspendLayout();
            menu.FormBorderStyle = FormBorderStyle.None;
            menu.ControlBox = false;
            menu.MinimizeBox = false;
            menu.MaximizeBox = false;
            menu.ShowInTaskbar = false;
            menu.AllowTransparency = true;
            menu.TransparencyKey = System.Drawing.Color.CornflowerBlue;
            menu.BackColor = System.Drawing.Color.CornflowerBlue;
            int offset = 0;
            int iconsCount = 0;
            Regex regex = new Regex("Icons/" + IconsPlugin.Guid + @"/(.+)\.(P|p)(N|n)(G|g)$");
            foreach (String icon in FileAccessPlugin.File.Catalog())
            {
                if (regex.IsMatch(icon))
                {
                    iconsCount++;
                    string iconFile = icon;
                    PictureBox iconImage = new PictureBox();
                    iconImage.SizeMode = PictureBoxSizeMode.AutoSize;
                    iconImage.Load(FileAccessPlugin.File.Find(icon)[0]);
                    iconImage.Top = 5;
                    iconImage.Left = offset;
                    iconImage.Click += (s, e) => { menu.Close(); ToggleIcon(cid, iconFile); };
                    menu.Controls.Add(iconImage);
                    offset = offset + 70;
                }
            }
            menu.Width = 70 * iconsCount;
            menu.Height = 100;
            menu.ResumeLayout();
            menu.StartPosition = FormStartPosition.CenterScreen;
            menu.Show();
            if (diagnosticLevel.Value >= DiagnostiocLevel.ultra) { Debug.Log("Menu Focus..."); }
            menu.Focus();
        }

        /// <summary>
        /// Method for applying icon toggle from sub-menu
        /// </summary>
        /// <param name="cid"></param>
        /// <param name="iconFile"></param>
        private void ToggleIcon(CreatureGuid cid, string iconFile)
        {
            if (diagnosticLevel.Value >= DiagnostiocLevel.medium) { Debug.Log("Icons Plugin: Toggling Icon (" + iconFile + ") State On Creature " + cid.ToString()); }
            UpdateIconList(cid.ToString(), iconFile);
        }

        /// <summary>
        /// Method to post the icon list for a creature to all players
        /// </summary>
        /// <param name="cid">Creature guid</param>
        /// <param name="iconFile">String name of the toggled icon</param>
        private void UpdateIconList(string cid, string iconFile)
        {
            // Get icon name without path or extension to use in the icon list
            iconFile = System.IO.Path.GetFileNameWithoutExtension(iconFile);
            // Read the current creature's icon list
            string iconList = AssetDataPlugin.ReadInfo(cid, IconsPlugin.Guid);
            if (iconList == null) { iconList = ""; }
            // Toggle icon in the icon list
            if (iconList.Contains("[" + iconFile + "]"))
            {
                // If the icon exits remove it
                iconList = iconList.Replace("[" + iconFile + "]", "");
            }
            else
            {
                // If the icon does not exit add it
                iconList = iconList + "[" + iconFile + "]";
            }
            // Post new creature list for the specified creature id
            AssetDataPlugin.SetInfo(cid, IconsPlugin.Guid, iconList);
        }

        /// <summary>
        /// Method used to process Stat Messages
        /// </summary>
        /// <param name="changes">Change parameter with the Stat Message content information</param>
        public void HandleRequest(AssetDataPlugin.DatumChange[] changes)
        {
            if (boardReady == BoardState.boardNotReady)
            {
                if (diagnosticLevel.Value >= DiagnostiocLevel.high) { Debug.Log("Icons Plugin: Adding Request To Backlog"); }
                backlog.AddRange(changes);
            }
            else
            {
                foreach (AssetDataPlugin.DatumChange change in changes)
                {
                    try
                    {
                        if (change.key == IconsPlugin.Guid)
                        {
                            // Update the icons on the specified mini
                            string iconList = AssetDataPlugin.ReadInfo(change.source, IconsPlugin.Guid);
                            if (diagnosticLevel.Value >= DiagnostiocLevel.high) { Debug.Log("Icons Plugin: Icons for Creature '" + change.source + "' have changed to '" + iconList + "'"); }
                            if (iconList != "")
                            {
                                // [a][b][c]
                                iconList = iconList.Substring(1);
                                iconList = iconList.Substring(0, iconList.Length - 1);
                                string[] icons = iconList.Split(new string[] { "][" }, StringSplitOptions.RemoveEmptyEntries);
                                UpdateActiveIcons(new CreatureGuid(change.source), icons);
                            }
                            else
                            {
                                UpdateActiveIcons(new CreatureGuid(change.source), new string[] { });
                            }
                        }
                    }
                    catch(Exception)
                    {
                        if (diagnosticLevel.Value >= DiagnostiocLevel.high) { Debug.Log("Icons Plugin: Failure to process change. Placing back on backlog."); }
                        backlog.Add(change);
                    }
                }
            }
        }

        /// <summary>
        /// Method to display icons. Creates up to 3 icon image objects as needed.
        /// Game objects are automatically created when needed and destroyed when not needed.
        /// </summary>
        /// <param name="cid">Creature guid for the creature to display icons</param>
        /// <param name="iconFiles">String representing the icons to be displayed wrapped in square brackets</param>
        private void UpdateActiveIcons(CreatureGuid cid, string[] iconFiles)
        {
            if (diagnosticLevel.Value >= DiagnostiocLevel.high) { Debug.Log("Icons Plugin: Updating Creature '" + cid + "' Icons"); } 
            CreatureBoardAsset asset;
            CreaturePresenter.TryGetAsset(cid, out asset);
            iconifiedAssets.Remove(cid);

            // Destroy previou icons (if any)
            GameObject.Destroy(GameObject.Find("StateIcon0:" + cid.ToString()));
            GameObject.Destroy(GameObject.Find("StateIcon1:" + cid.ToString()));
            GameObject.Destroy(GameObject.Find("StateIcon2:" + cid.ToString()));

            // If there are no icons, exit now since we already destroyed all previous icons
            if (iconFiles.Length == 0) { return; }

            // Get parent object to which the icons will be attached
            Transform parentTransform = Utility.GetAssetLoader(asset.CreatureId).transform;
            iconifiedAssets.Add(cid);

            if (asset != null)
            {
                GameObject[] icon = new GameObject[3];

                for (int i = 0; i < iconFiles.Length; i++)
                {
                    // Create new icon 
                    icon[i] = new GameObject();
                    icon[i].name = "StateIcon" + i + ":" + cid.ToString();
                    Canvas canvas = icon[i].AddComponent<Canvas>();
                    Image img = icon[i].AddComponent<Image>();
                    img.transform.SetParent(canvas.transform);
                    // Load icon image
                    icon[i].GetComponent<Image>().sprite = FileAccessPlugin.Image.LoadSprite("Icons/" + IconsPlugin.Guid + "/" + iconFiles[i] + ".PNG");
                    // Set scale
                    icon[i].GetComponent<Image>().transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                    icon[i].SetActive(true);
                    icon[i].transform.SetParent(parentTransform);
                }

                if (diagnosticLevel.Value >= DiagnostiocLevel.ultra) { Debug.Log("Icons Plugin: Updating Creature '" + cid + "' Icons Position"); }
                SyncBaseWithIcons(cid);
            }
        }

        /// <summary>
        /// Method to update icons position to their respective bases and turn them so that they are always facing the user
        /// </summary>
        /// <param name="cid">Creature id of the mini to be syned</param>
        private void SyncBaseWithIcons(CreatureGuid cid)
        {
            if (cid == null) { return; }
            if (cid == CreatureGuid.Empty) { return; }
            CreatureBoardAsset asset;
            CreaturePresenter.TryGetAsset(cid, out asset);
            if (asset == null) { return; }

            icon0 = GameObject.Find("StateIcon0:" + cid.ToString());
            icon1 = GameObject.Find("StateIcon1:" + cid.ToString());
            icon2 = GameObject.Find("StateIcon2:" + cid.ToString());

            if ((icon0==null)&& (icon0 == null)&& (icon0 == null)) 
            {
                return;
            }

            // Check for Stealth mode entry
            if (asset.IsExplicitlyHidden && icon0!=null)
            {
                if (icon0 != null) { icon0.GetComponent<Canvas>().enabled = false; }
                if (icon1 != null) { icon1.GetComponent<Canvas>().enabled = false; }
                if (icon2 != null) { icon2.GetComponent<Canvas>().enabled = false; }
            }
            // Check for Stealth mode exit
            else if (!asset.IsExplicitlyHidden && icon0==null)
            {
                if (icon0 != null) { icon0.GetComponent<Canvas>().enabled = true; }
                if (icon1 != null) { icon1.GetComponent<Canvas>().enabled = true; }
                if (icon2 != null) { icon2.GetComponent<Canvas>().enabled = true; }
            }

            // Don't sync icon when in stealth mode
            if (asset.IsExplicitlyHidden) { return; }

            Vector3 scale = new Vector3((float)Math.Sqrt(asset.Scale) / 1000f, (float)Math.Sqrt(asset.Scale) / 1000f, (float)Math.Sqrt(asset.Scale) / 1000f);

            // Get reference to base
            Transform baseTransform = Utility.GetBaseLoader(asset.CreatureId).transform;

            // Sync icons with base
            if ((icon0 != null) && (icon1 == null) && (icon2 == null))
            {
                offset0 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(0, 0.05f * (float)Math.Sqrt(asset.Scale), -0.45f) * asset.Scale;
                icon0.transform.position = offset0 + baseTransform.position;
                icon0.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y, 0);
                icon0.transform.localScale = scale;
            }
            else if ((icon0 != null) && (icon1 != null) && (icon2 == null))
            {
                offset0 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(0.125f, 0.05f * (float)Math.Sqrt(asset.Scale), -0.425f) * asset.Scale;
                offset1 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(-0.125f, 0.05f * (float)Math.Sqrt(asset.Scale), -0.425f) * asset.Scale;
                icon0.transform.position = offset0 + baseTransform.position;
                icon0.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y - 20, 0);
                icon1.transform.position = offset1 + baseTransform.position;
                icon1.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y + 20, 0);
                icon0.transform.localScale = scale;
                icon1.transform.localScale = scale;
            }
            else
            {
                offset0 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(+0.125f, +0.05f * (float)Math.Sqrt(asset.Scale), -0.425f) * asset.Scale;
                offset1 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(0, 0.05f * (float)Math.Sqrt(asset.Scale), -0.45f) * asset.Scale;
                offset2 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(-0.125f, +0.05f * (float)Math.Sqrt(asset.Scale), -0.425f) * asset.Scale;
                icon0.transform.position = offset0 + baseTransform.position;
                icon0.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y - 20, 0);
                icon1.transform.position = offset1 + baseTransform.position;
                icon1.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y, 0);
                icon2.transform.position = offset2 + baseTransform.position;
                icon2.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y + 20, 0);
                icon0.transform.localScale = scale;
                icon1.transform.localScale = scale;
                icon2.transform.localScale = scale;
            }
        }

        public IEnumerator DelayIconProcessing(object[] inputs)
        {
            int passes = 0;
            while (boardReady == BoardState.boardIconsBuildDelay)
            {
                passes++;
                if (diagnosticLevel.Value >= DiagnostiocLevel.ultra) { Debug.Log("Icons Plugin: Backlog Pass " + passes); }
                yield return new WaitForSeconds((float)inputs[0]);
                AssetDataPlugin.DatumChange[] changes = backlog.ToArray();
                backlog.Clear();
                if (diagnosticLevel.Value >= DiagnostiocLevel.high) { Debug.Log("Icons Plugin: Processing Backlog Of "+changes.Length+" Items"); }
                HandleRequest(changes);
                if (backlog.Count == 0) 
                {
                    if (diagnosticLevel.Value >= DiagnostiocLevel.high) { Debug.Log("Icons Plugin: Backlog processed."); }
                    boardReady = BoardState.boardReady;
                    break; 
                }
                if (passes >= 10) 
                { 
                    backlog.Clear();
                    Debug.LogWarning("Icons Plugin: Unable to process backlog. Removing.");
                    break; 
                }
            }
        }
    }
}

