using BepInEx;
using UnityEngine;
using UnityEngine.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using BepInEx.Configuration;
using System.Reflection;

namespace LordAshes
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(LordAshes.StatMessaging.Guid)]
    [BepInDependency(RadialUI.RadialUIPlugin.Guid)]
    public partial class IconsPlugin : BaseUnityPlugin
    {
        // Plugin info
        public const string Name = "Icons Plug-In";
        public const string Guid = "org.lordashes.plugins.icons";
        public const string Version = "1.2.0.0";

        // Configuration
        private ConfigEntry<KeyboardShortcut> triggerKey { get; set; }
        private ConfigEntry<bool> performanceHigh { get; set; }

        // Content directory
        private string dir = UnityEngine.Application.dataPath.Substring(0, UnityEngine.Application.dataPath.LastIndexOf("/")) + "/TaleSpire_CustomData/";

        // Data Change Handlers
        private List<CreatureGuid> iconifiedAssets = new List<CreatureGuid>();


        // Active board
        private bool boardActive = false;

        // CreatureMoveBoardTool reference for determining drops
        private BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

        // Selected item
        private CreatureGuid heldMini = CreatureGuid.Empty;

        // Variables used by the synchronization function. Exposed here so that the function does not constantly allocate and deallocate memory
        GameObject icon0;
        GameObject icon1;
        GameObject icon2;
        Vector3 offset0;
        Vector3 offset1;
        Vector3 offset2;

        /// <summary>
        /// Function for initializing plugin
        /// This function is called once by TaleSpire
        /// </summary>
        void Awake()
        {
            UnityEngine.Debug.Log("Lord Ashes Icons Plugin Active.");

            triggerKey = Config.Bind("Hotkeys", "Icons Toggle Menu", new KeyboardShortcut(KeyCode.I, KeyCode.LeftControl));
            performanceHigh = Config.Bind("Settings", "Use High Performance (uses higher CPU load)", false);

            BoardSessionManager.OnStateChange += (s) =>
            {
                if (s.ToString().Contains("+Active")) {  boardActive = true;  }  else { boardActive = false;  }
            };

            // Add Info menu selection to main character menu
            RadialUI.RadialSubmenu.EnsureMainMenuItem(  RadialUI.RadialUIPlugin.Guid + ".Info",
                                                        RadialUI.RadialSubmenu.MenuType.character,
                                                        "Info",
                                                        RadialUI.RadialSubmenu.GetIconFromFile(dir+"Images/Icons/Info.png")
                                                     );

            // Add Icons sub menu item
            RadialUI.RadialSubmenu.CreateSubMenuItem(   RadialUI.RadialUIPlugin.Guid+".Info",
                                                        "Icons",
                                                        RadialUI.RadialSubmenu.GetIconFromFile(dir + "Images/Icons/Icons.png"),
                                                        SetRequest,
                                                        false
                                                    );

            // Subscribe to Stat Messages
            StatMessaging.Subscribe(IconsPlugin.Guid, HandleRequest);

            // Display plugin on the main TaleSpire page
            StateDetection.Initialize(this.GetType());
        }

        /// <summary>
        /// Function for determining if view mode has been toggled and, if so, activating or deactivating Character View mode.
        /// This function is called periodically by TaleSpire.
        /// </summary>
        void Update()
        {
            if (boardActive && StateDetection.Ready())
            {
                CreatureBoardAsset asset = null;

                // Check for states menu trigger
                if (triggerKey.Value.IsUp())
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
                    heldMini = asset.Creature.CreatureId;
                }
                else if(heldMini != CreatureGuid.Empty)
                {
                    Debug.Log("Drop Event For '" + heldMini + "'...");
                    StatMessaging.SetInfo(heldMini, IconsPlugin.Guid + ".Update", DateTime.UtcNow.ToString());
                    heldMini = CreatureGuid.Empty;
                }
            }
        }

        /// <summary>
        /// Method used to process Stat Messages
        /// </summary>
        /// <param name="changes">Change parameter with the Stat Message content information</param>
        public void HandleRequest(StatMessaging.Change[] changes)
        {
            foreach (StatMessaging.Change change in changes)
            {
                if (change.key == IconsPlugin.Guid)
                {
                    // Update the icons on the specified mini
                    try
                    {
                        string iconList = StatMessaging.ReadInfo(change.cid, IconsPlugin.Guid);
                        Debug.Log("Icons for Creature '"+change.cid+"' have changed to '" + iconList + "'");
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
                else if (change.key == IconsPlugin.Guid+".Update")
                {
                    // Synchronized position of icons with respect to the specified mini
                    try
                    {
                        SyncBaseWithIcons(change.cid);
                    }
                    catch(Exception) {; }
                }
            }
        }

        /// <summary>
        /// Callback method called from sub-menu selection
        /// </summary>
        /// <param name="cid">Creature Guid associated with radial menu asset</param>
        /// <param name="menu">Menu Guid</param>
        /// <param name="mmi">MapMenuItem associated with the menu</param>
        private void SetRequest(CreatureGuid cid, string menu, MapMenuItem mmi)
        {
            // Create sub-menu
            MapMenu mapMenu = MapMenuManager.OpenMenu(mmi, MapMenu.MenuType.SUBROOT);
            // Populate sub-menu based on all items added by any plugins for the specific main menu entry
            foreach (string iconFile in System.IO.Directory.EnumerateFiles(dir + "Images/Icons/" + IconsPlugin.Guid, "*.PNG"))
            {
                Texture2D tex = new Texture2D(64, 64);
                tex.LoadImage(System.IO.File.ReadAllBytes(iconFile));
                Sprite icon = Sprite.Create(Scale64To32(tex), new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));

                mapMenu.AddItem(new MapMenu.ItemArgs()
                {
                    Action = (_mmi,_obj)=> { ToggleIcon(cid, iconFile); },
                    Icon = icon,
                    Title = System.IO.Path.GetFileNameWithoutExtension(iconFile),
                    CloseMenuOnActivate = true
                });
            }
        }

        /// <summary>
        /// Method for applying icon toggle from sub-menu
        /// </summary>
        /// <param name="cid"></param>
        /// <param name="iconFile"></param>
        private void ToggleIcon(CreatureGuid cid, string iconFile)
        {
            SyncIconList(cid, iconFile);
        }

        /// <summary>
        /// Method to write stats to the Creature Name
        /// </summary>
        public void SetRequest(CreatureGuid cid)
        {
            string[] icons = System.IO.Directory.EnumerateFiles(dir + "Images/Icons/" + IconsPlugin.Guid, "*.PNG").ToArray();
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
            menu.Width = 70 * icons.Length;
            menu.Height = 100;
            int offset = 0;
            foreach (string icon in icons)
            {
                string iconFile = icon;
                PictureBox iconImage = new PictureBox();
                iconImage.SizeMode = PictureBoxSizeMode.AutoSize;
                iconImage.Load(icon);
                iconImage.Top = 5;
                iconImage.Left = offset;
                iconImage.Click += (s, e) => { menu.Close(); SyncIconList(cid, iconFile); };
                menu.Controls.Add(iconImage);
                offset = offset + 70;
            }
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
            CreatureBoardAsset asset;
            CreaturePresenter.TryGetAsset(cid, out asset);
            icon0 = GameObject.Find("StateIcon0:" + cid);
            icon1 = GameObject.Find("StateIcon1:" + cid);
            icon2 = GameObject.Find("StateIcon2:" + cid);

            // Check for Stealth mode entry
            if (asset.Creature.IsExplicitlyHidden && icon0!=null)
            {
                Debug.Log("Stealth Mode Entered: Hiding Icons");
                GameObject.Destroy(GameObject.Find("StateIcon0:" + cid));
                GameObject.Destroy(GameObject.Find("StateIcon1:" + cid));
                GameObject.Destroy(GameObject.Find("StateIcon2:" + cid));
            }
            // Check for Stealth mode exit
            else if (!asset.Creature.IsExplicitlyHidden && icon0==null)
            {
                Debug.Log("Stealth Mode Exited: Revealing Icons");
                string iconList = StatMessaging.ReadInfo(cid, IconsPlugin.Guid);
                // [a][b][c]
                iconList = iconList.Substring(1);
                iconList = iconList.Substring(0, iconList.Length - 1);
                string[] icons = iconList.Split(new string[] { "][" }, StringSplitOptions.RemoveEmptyEntries);
                DisplayIcons(cid, icons);
            }

            // Don't sync icon when in stealth mode
            if (asset.Creature.IsExplicitlyHidden) { return; }

            Vector3 scale = new Vector3(asset.Creature.Scale / 1000, asset.Creature.Scale / 1000, asset.Creature.Scale / 1000);

            // Sync icons with base
            if ((icon0 != null) && (icon1 == null) && (icon2 == null))
            {
                offset0 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(0, 0.05f, -0.45f) * asset.Creature.Scale;
                icon0.transform.position = offset0 + asset.BaseLoader.LoadedAsset.transform.position;
                icon0.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y, 0);
                icon0.transform.localScale = scale;
                icon0.SetActive(!asset.Creature.IsExplicitlyHidden);
            }
            else if ((icon0 != null) && (icon1 != null) && (icon2 == null))
            {
                offset0 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(0.125f, 0.05f, -0.425f) * asset.Creature.Scale;
                offset1 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(-0.125f, 0.05f, -0.425f) * asset.Creature.Scale;
                icon0.transform.position = offset0 + asset.BaseLoader.LoadedAsset.transform.position;
                icon0.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y - 20, 0);
                icon1.transform.position = offset1 + asset.BaseLoader.LoadedAsset.transform.position;
                icon1.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y + 20, 0);
                icon0.transform.localScale = scale;
                icon1.transform.localScale = scale;
                icon0.SetActive(!asset.Creature.IsExplicitlyHidden);
                icon1.SetActive(!asset.Creature.IsExplicitlyHidden);
            }
            else
            {
                offset0 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(+0.125f, +0.05f, -0.425f) * asset.Creature.Scale;
                offset1 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(0, 0.05f, -0.45f) * asset.Creature.Scale;
                offset2 = Quaternion.AngleAxis(Camera.main.transform.eulerAngles.y, Vector3.up) * new Vector3(-0.125f, +0.05f, -0.425f) * asset.Creature.Scale;
                icon0.transform.position = offset0 + asset.BaseLoader.LoadedAsset.transform.position;
                icon0.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y - 20, 0);
                icon1.transform.position = offset1 + asset.BaseLoader.LoadedAsset.transform.position;
                icon1.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y, 0);
                icon2.transform.position = offset2 + asset.BaseLoader.LoadedAsset.transform.position;
                icon2.transform.eulerAngles = new Vector3(20, Camera.main.transform.eulerAngles.y + 20, 0);
                icon0.transform.localScale = scale;
                icon1.transform.localScale = scale;
                icon2.transform.localScale = scale;
                icon0.SetActive(!asset.Creature.IsExplicitlyHidden);
                icon1.SetActive(!asset.Creature.IsExplicitlyHidden);
                icon2.SetActive(!asset.Creature.IsExplicitlyHidden);
            }
        }

        /// <summary>
        /// Method to display icons. Creates up to 3 icon image objects as needed.
        /// Game objects are automatically created when needed and destroyed when not needed.
        /// </summary>
        /// <param name="cid">Creature guid for the creature to display icons</param>
        /// <param name="iconFiles">String representing the icons to be displayed wrappedn in square brackets</param>
        private void DisplayIcons(CreatureGuid cid, string[] iconFiles)
        {
            Debug.Log("Updating Creature '" + cid + "' Icons");
            CreatureBoardAsset asset;
            CreaturePresenter.TryGetAsset(cid, out asset);
            iconifiedAssets.Remove(cid);

            // Desatroy previou icons (if any)
            GameObject.Destroy(GameObject.Find("StateIcon0:" + cid));
            GameObject.Destroy(GameObject.Find("StateIcon1:" + cid));
            GameObject.Destroy(GameObject.Find("StateIcon2:" + cid));

            // If there are no icons, exit now since we already destroyed all previous icons
            if (iconFiles.Length == 0) { return; }

            // Get parent object to which the icons will be attached
            AssetLoader parent = asset.BaseLoader;
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
                    icon[i].GetComponent<Image>().sprite = Sprite.Create(LoadTexture(dir+"Images/Icons/"+IconsPlugin.Guid+"/"+iconFiles[i]+".PNG"), new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100);
                    // Set scale
                    icon[i].GetComponent<Image>().transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                    icon[i].SetActive(true);
                }

                Debug.Log("Updating Creature '" + cid + "' Icons Position");
                SyncBaseWithIcons(cid);
            }
        }

        /// <summary>
        /// Method to load a Texture2D from a file
        /// </summary>
        /// <param name="FilePath"></param>
        /// <returns></returns>
        public Texture2D LoadTexture(string FilePath)
        {
            Texture2D Tex2D;
            byte[] FileData;

            if (System.IO.File.Exists(FilePath))
            {
                FileData = System.IO.File.ReadAllBytes(FilePath);
                Tex2D = new Texture2D(2, 2);
                if (Tex2D.LoadImage(FileData)) return Tex2D;             
            }
            return null;                     
        }

        /// <summary>
        /// Function to check if the board is loaded
        /// </summary>
        /// <returns></returns>
        public bool isBoardLoaded()
        {
            return CameraController.HasInstance && BoardSessionManager.HasInstance && !BoardSessionManager.IsLoading;
        }

        /// <summary>
        /// Method to scale 64x64 icons to 32x32
        /// </summary>
        /// <param name="src">Texture2D that is 64x64</param>
        /// <returns>Tetxure2D that is 32x32</returns>
        public Texture2D Scale64To32(Texture2D src)
        {
            Texture2D tex = new Texture2D(32, 32);
            for(int y=0; y<32; y++)
            {
                for(int x=0; x<32; x++)
                {
                    Color c = src.GetPixel(x * 2, y * 2);
                    tex.SetPixel(x,y,c);
                }
            }
            tex.Apply();
            return tex;
        }
    }
}

