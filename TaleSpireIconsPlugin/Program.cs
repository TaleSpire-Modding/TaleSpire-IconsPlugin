using BepInEx;
using UnityEngine;
using UnityEngine.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using BepInEx.Configuration;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Collections;
using Newtonsoft.Json;

namespace LordAshes
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(LordAshes.StatMessaging.Guid)]
    [BepInDependency(RadialUI.RadialUIPlugin.Guid)]
    [BepInDependency(LordAshes.FileAccessPlugin.Guid)]
    public partial class IconsPlugin : BaseUnityPlugin
    {
        // Plugin info
        public const string Name = "Icons Plug-In";
        public const string Guid = "org.lordashes.plugins.icons";
        public const string Version = "2.0.0.0";

        // Configuration
        private ConfigEntry<KeyboardShortcut> triggerIconsMenu { get; set; }
        private ConfigEntry<bool> performanceHigh { get; set; }

        // Content directory
        private string dir = UnityEngine.Application.dataPath.Substring(0, UnityEngine.Application.dataPath.LastIndexOf("/")) + "/TaleSpire_CustomData/";

        // Data Change Handlers
        private List<CreatureGuid> iconifiedAssets = new List<CreatureGuid>();

        // CreatureMoveBoardTool reference for determining drops
        private BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

        // Selected item
        private CreatureGuid heldMini = CreatureGuid.Empty;

        // Subscription
        private static float delayIconProcessing = 3.0f;
        private static BoardState boardReady = BoardState.boardNotReady;
        private List<StatMessaging.Change> backlog = new List<StatMessaging.Change>();

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
            UnityEngine.Debug.Log("Icons Plugin: "+GetType().AssemblyQualifiedName+" is Active.");

            triggerIconsMenu = Config.Bind("Hotkeys", "Icons Toggle Menu", new KeyboardShortcut(KeyCode.I, KeyCode.LeftControl));
            performanceHigh = Config.Bind("Settings", "Use High Performance (uses higher CPU load)", true);
            delayIconProcessing = Config.Bind("Setting", "Delay Icon Processing On Startup", 3.0f).Value;

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
                Debug.Log("Icons Plugin: Comparing '"+iconFile+"' To Regex");
                if (regex.IsMatch(iconFile))
                {
                    Debug.Log("Icons Plugin: Found Icons '"+iconFile+"'");
                    RadialUI.RadialSubmenu.CreateSubMenuItem(RadialUI.RadialUIPlugin.Guid + ".Icons",
                                                                System.IO.Path.GetFileNameWithoutExtension(iconFile),
                                                                FileAccessPlugin.Image.LoadSprite(iconFile),
                                                                (a,b,c)=> { ToggleIcon(a,b,c,iconFile); },
                                                                true,
                                                                () => { Debug.Log("Icons Plugin: Adding Icon '"+iconFile+"'");  return LocalClient.HasControlOfCreature(new CreatureGuid(RadialUI.RadialUIPlugin.GetLastRadialTargetCreature())); }
                                                            );
                }
            }

            StatMessaging.Subscribe(IconsPlugin.Guid, HandleRequest);

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
                if(boardReady==BoardState.boardNotReady)
                {
                    boardReady = BoardState.boardIconsBuildDelay;
                    Debug.Log("Icons Plugin: Board Loaded Delaying Message Processing To Allow Minis To Load");
                    StartCoroutine("DelayIconProcessing", new object[] { delayIconProcessing });
                }

                CreatureBoardAsset asset = null;

                // Check for states menu trigger
                if (Utility.StrictKeyCheck(triggerIconsMenu.Value))
                {
                    SetRequest(LocalClient.SelectedCreatureId);
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
                    foreach(CreatureGuid mini in iconifiedAssets)
                    {
                        SyncBaseWithIcons(mini);
                    }
                }

                // Detect mini drop so that a sync request can be sent to client
                CreatureMoveBoardTool moveBoard = SingletonBehaviour<BoardToolManager>.Instance.GetTool<CreatureMoveBoardTool>();
                asset = (CreatureBoardAsset)typeof(CreatureMoveBoardTool).GetField("_pickupObject", flags).GetValue(moveBoard);
                if (asset != null)
                { 
                    heldMini = asset.CreatureId;
                }
                else if(heldMini != CreatureGuid.Empty)
                {
                    Debug.Log("Icons Plugin: Drop Event For '" + heldMini + "'...");
                    StatMessaging.SetInfo(heldMini, IconsPlugin.Guid + ".Update", DateTime.UtcNow.ToString());
                    heldMini = CreatureGuid.Empty;
                }
            }
            else
            {
                if (boardReady != BoardState.boardNotReady)
                {
                    Debug.Log("Icons Plugin: Board Unloaded");
                    boardReady = BoardState.boardNotReady;
                }
            }
        }

        /// <summary>
        /// Method used to process Stat Messages
        /// </summary>
        /// <param name="changes">Change parameter with the Stat Message content information</param>
        public void HandleRequest(StatMessaging.Change[] changes)
        {
            if (boardReady < BoardState.boardReady)
            {
                Debug.Log("Icons Plugin: Adding Request To Backlog");
                backlog.AddRange(changes);
            }
            else
            {
                foreach (StatMessaging.Change change in changes)
                {
                    if (change.key == IconsPlugin.Guid)
                    {
                        // Update the icons on the specified mini
                        try
                        {
                            string iconList = StatMessaging.ReadInfo(change.cid, IconsPlugin.Guid);
                            Debug.Log("Icons Plugin: Icons for Creature '" + change.cid + "' have changed to '" + iconList + "'");
                            if (iconList != "")
                            {
                                // [a][b][c]
                                iconList = iconList.Substring(1);
                                iconList = iconList.Substring(0, iconList.Length - 1);
                                string[] icons = iconList.Split(new string[] { "][" }, StringSplitOptions.RemoveEmptyEntries);
                                DisplayIcons(change.cid, icons);
                            }
                            else
                            {
                                DisplayIcons(change.cid, new string[] { });
                            }
                        }
                        catch (Exception) {; }
                    }
                    else if (change.key == IconsPlugin.Guid + ".Update")
                    {
                        // Synchronized position of icons with respect to the specified mini
                        try
                        {
                            SyncBaseWithIcons(change.cid);
                        }
                        catch (Exception) {; }
                    }
                }
            }
        }

        /// <summary>
        /// Method for applying icon toggle from sub-menu
        /// </summary>
        /// <param name="cid"></param>
        /// <param name="iconFile"></param>
        private void ToggleIcon(CreatureGuid cid, string arg, MapMenuItem mmi, string iconFile)
        {
            Debug.Log("Icons Plugin: Toggling Icon ("+iconFile+") State On Creature "+cid);
            SyncIconList(cid, iconFile);
        }

        /// <summary>
        /// Method to write stats to the Creature Name
        /// </summary>
        public void SetRequest(CreatureGuid cid)
        {
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
                    iconImage.Click += (s, e) => { menu.Close(); SyncIconList(cid, iconFile); };
                    menu.Controls.Add(iconImage);
                    offset = offset + 70;
                }
            }
            menu.Width = 70 * iconsCount;
            menu.Height = 100;
            menu.ResumeLayout();
            menu.StartPosition = FormStartPosition.CenterScreen;
            menu.Show();
            Debug.Log("Menu Focus...");
            menu.Focus();
        }

        /// <summary>
        /// Method to post the icon list for a creature to all players using Stat Messaging
        /// </summary>
        /// <param name="cid">Creature guid</param>
        /// <param name="iconFile">String name of the toggled icon</param>
        private void SyncIconList(CreatureGuid cid, string iconFile)
        {
            // Get icon name without path or extension to use in the icon list
            iconFile = System.IO.Path.GetFileNameWithoutExtension(iconFile);
            // Read the current creature's icon list
            string iconList = StatMessaging.ReadInfo(cid, IconsPlugin.Guid);
            // Toggle icon in the icon list
            if(iconList.Contains("["+iconFile+"]"))
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
            StatMessaging.SetInfo(cid, IconsPlugin.Guid, iconList);
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
            icon0 = GameObject.Find("StateIcon0:" + cid);
            icon1 = GameObject.Find("StateIcon1:" + cid);
            icon2 = GameObject.Find("StateIcon2:" + cid);

            // Check for Stealth mode entry
            if (asset.IsExplicitlyHidden && icon0!=null)
            {
                Debug.Log("Icons Plugin: Stealth Mode Entered: Hiding Icons");
                GameObject.Destroy(GameObject.Find("StateIcon0:" + cid));
                GameObject.Destroy(GameObject.Find("StateIcon1:" + cid));
                GameObject.Destroy(GameObject.Find("StateIcon2:" + cid));
            }
            // Check for Stealth mode exit
            else if (!asset.IsExplicitlyHidden && icon0==null)
            {
                Debug.Log("Icons Plugin: Stealth Mode Exited: Revealing Icons");
                string iconList = StatMessaging.ReadInfo(cid, IconsPlugin.Guid);
                // [a][b][c]
                iconList = iconList.Substring(1);
                iconList = iconList.Substring(0, iconList.Length - 1);
                string[] icons = iconList.Split(new string[] { "][" }, StringSplitOptions.RemoveEmptyEntries);
                DisplayIcons(cid, icons);
            }

            // Don't sync icon when in stealth mode
            if (asset.IsExplicitlyHidden) { return; }

            Vector3 scale = new Vector3((float)Math.Sqrt(asset.Scale) / 1000f, (float)Math.Sqrt(asset.Scale) / 1000f, (float)Math.Sqrt(asset.Scale) / 1000f);

            // Get reference to base
            Transform baseTransform = Utility.GetBaseObject(asset.CreatureId).transform;

            // Sync icons with base
            if ((icon0 != null) && (icon1 == null) && (icon2 == null))
            {
                offset0 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(0, 0.05f * (float)Math.Sqrt(asset.Scale), -0.45f) * asset.Scale;
                icon0.transform.position = offset0 + baseTransform.position;
                icon0.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y, 0);
                icon0.transform.localScale = scale;
                icon0.SetActive(!asset.IsExplicitlyHidden);
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
                icon0.SetActive(!asset.IsExplicitlyHidden);
                icon1.SetActive(!asset.IsExplicitlyHidden);
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
                icon0.SetActive(!asset.IsExplicitlyHidden);
                icon1.SetActive(!asset.IsExplicitlyHidden);
                icon2.SetActive(!asset.IsExplicitlyHidden);
            }
        }

        /// <summary>
        /// Method to display icons. Creates up to 3 icon image objects as needed.
        /// Game objects are automatically created when needed and destroyed when not needed.
        /// </summary>
        /// <param name="cid">Creature guid for the creature to display icons</param>
        /// <param name="iconFiles">String representing the icons to be displayed wrapped in square brackets</param>
        private void DisplayIcons(CreatureGuid cid, string[] iconFiles)
        {
            Debug.Log("Icons Plugin: Updating Creature '" + cid + "' Icons");
            CreatureBoardAsset asset;
            CreaturePresenter.TryGetAsset(cid, out asset);
            iconifiedAssets.Remove(cid);

            // Destroy previou icons (if any)
            GameObject.Destroy(GameObject.Find("StateIcon0:" + cid));
            GameObject.Destroy(GameObject.Find("StateIcon1:" + cid));
            GameObject.Destroy(GameObject.Find("StateIcon2:" + cid));

            // If there are no icons, exit now since we already destroyed all previous icons
            if (iconFiles.Length == 0) { return; }

            // Get parent object to which the icons will be attached
            Transform parentTransform = Utility.GetBaseObject(asset.CreatureId).transform;
            iconifiedAssets.Add(cid);

            if (asset!=null)
            {
                GameObject[] icon = new GameObject[3];

                for (int i = 0; i < iconFiles.Length; i++)
                {
                    // Create new icon 
                    icon[i] = new GameObject();
                    icon[i].name = "StateIcon" + i + ":" + cid;
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

                Debug.Log("Icons Plugin: Updating Creature '" + cid + "' Icons Position");
                SyncBaseWithIcons(cid);
            }
        }

        public IEnumerator DelayIconProcessing(object[] inputs)
        {
            // Subscribe to Stat Messages
            yield return new WaitForSeconds((float)inputs[0]);
            boardReady = BoardState.boardReady;
            Debug.Log("Icons Plugin: Processing Backlog");
            HandleRequest(backlog.ToArray());
            backlog.Clear();
        }
    }
}

