﻿using OpenALPRPlugin.Background;
using OpenALPRPlugin.Forms;
using OpenALPRPlugin.Properties;
using OpenALPRPlugin.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Client;
using VideoOS.Platform.Data;
using VideoOS.Platform.Messaging;
using VideoOS.Platform.UI;

namespace OpenALPRPlugin.Client
{
    public partial class WorkSpaceControl : ViewItemUserControl
    {
        private object _themeChangedReceiver;
        private ContextMenu cmBookmarks;
        private Item selectedCameraItem = null ;
        private const string Edit = "Edit";
        private const string Delete = "Delete";
        private const string View = "View";
        private const int bookmarksCount = 32;

        public WorkSpaceControl(OpenALPRViewItemManager viewItemManager)
        {
            _themeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(new MessageReceiver(ThemeChangedIndicationHandler),
                                             new MessageIdFilter(MessageId.SmartClient.ThemeChangedIndication));

            InitializeComponent();
            //Size = new Size(2200, 812);//Size = new Size(965, 812);

            ClientControl.Instance.RegisterUIControlForAutoTheming(this);

            lsvBookmarks.BackColor = ClientControl.Instance.Theme.BackgroundColor;
            lsvBookmarks.ForeColor = ClientControl.Instance.Theme.TextColor;

            
            //picOpenALPR.SizeMode = PictureBoxSizeMode.StretchImage;

            var fullDateTimePattern = CultureInfo.CurrentUICulture.DateTimeFormat.FullDateTimePattern;

            datStartTime.CustomFormat = fullDateTimePattern;
            datEndTime.CustomFormat = fullDateTimePattern;

            datStartTime.Value = DateTime.Now.AddDays(-2);
            datEndTime.Value = DateTime.Now;

            chkMyBookmarksOnly.Checked = OpenALPRBackgroundPlugin.MyOwnBookmarksOnly ;
            txtSearchFor.Text = OpenALPRBackgroundPlugin.SearchString;
            if (OpenALPRBackgroundPlugin.Bookmarks != null)
                AddToListView(OpenALPRBackgroundPlugin.Bookmarks);

            var savedCameraId = Settings1.Default.usedFQID;// = selectedCameraItem.FQID;
            if (savedCameraId != Guid.Empty)
            {
                var camera = Configuration.Instance.GetItem(EnvironmentManager.Instance.MasterSite.ServerId, savedCameraId, Kind.Camera);
                if (camera != null)
                {
                    txtCameraName.Text = camera.Name;
                    selectedCameraItem = camera;
                }
            }
        }

        public override void Init()
        {
            base.Init();
            ClientControl.Instance.RegisterUIControlForAutoTheming(this);
        }

        private void BookmarkViewItemManager_Load(object sender, EventArgs e)
        {
            if (ClientControl.Instance.Theme.ThemeType == ThemeType.Dark)
            {
                picOpenALPR.Image = Resources.logo_white_512x93;
                picOpenALPR.Refresh();
                Refresh();
            }

            cmBookmarks = new ContextMenu();
            cmBookmarks.MenuItems.Add(CreateMenuItem(Edit, new EventHandler(EditBookmark)));
            cmBookmarks.MenuItems.Add(CreateMenuItem(Delete, new EventHandler(DeleteBookmark)));
            cmBookmarks.MenuItems.Add(CreateMenuItem(View, new EventHandler(ViewData)));
        }

        private static MenuItem CreateMenuItem(string text, EventHandler eventHandler)
        {
            var item = new MenuItem(text, eventHandler)
            {
                Name = text
            };
            return item;
        }

        public override void Print()
        {
            Print(OpenALPRPluginDefinition.PlugName, "http://www.openalpr.com/");
        }

        /// <summary>
        /// Signals that the form is right clicked
        /// </summary>
        public event EventHandler RightClickEvent;

        /// <summary>
        /// Activates the RightClickEvent
        /// </summary>
        /// <param name="e">Event args</param>
        protected virtual void FireRightClickEvent(EventArgs e)
        {
            RightClickEvent?.Invoke(this, e);
        }

        private object ThemeChangedIndicationHandler(VideoOS.Platform.Messaging.Message message, FQID destination, FQID source)
        {
            Selected = _selected;

            lsvBookmarks.BackColor = ClientControl.Instance.Theme.BackgroundColor;
            lsvBookmarks.ForeColor = ClientControl.Instance.Theme.TextColor;

            picOpenALPR.Image = ClientControl.Instance.Theme.ThemeType == ThemeType.Dark ?
                Resources.logo_white_512x93 :
                Resources.logo_bluegray;
            picOpenALPR.Refresh();

            return null;
        }

        /// <summary>
        /// Gets boolean indicating whether the view item can be maximized or not. <br/>
        /// The content holder should implement the click and double click events even if it is not maximizable. 
        /// </summary>
        public override bool Maximizable
        {
            get { return true; }
        }

        /// <summary>
        /// Tell if ViewItem is selectable
        /// </summary>
        public override bool Selectable
        {
            get { return true; }
        }

        /// <summary>
        /// Make support for Theme colors to show if this ViewItem is selected or not.
        /// </summary>
        public override bool Selected
        {
            get { return base.Selected; }
            set { base.Selected = value; }
        }

        public override void Close()
        {
            OpenALPRBackgroundPlugin.MyOwnBookmarksOnly = chkMyBookmarksOnly.Checked;
            OpenALPRBackgroundPlugin.SearchString = txtSearchFor.Text;

            base.Close();

            EnvironmentManager.Instance.UnRegisterReceiver(_themeChangedReceiver);
            _themeChangedReceiver = null;

            if (cmBookmarks != null)
            {
                for (int i = cmBookmarks.MenuItems.Count - 1; i >= 0; i--)
                {
                    var item = cmBookmarks.MenuItems[i];
                    if (item != null)
                    {
                        try
                        {
                            DisposeObject(ref item);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log.Error(null, ex);
                        }
                    }
                }

                DisposeObject(ref cmBookmarks);
            }
        }

        private static void DisposeObject<T>(ref T obj) where T : class, IDisposable
        {
            if (obj != default(T))
            {
                obj.Dispose();
                obj = default(T);
            }
        }

        private async void Search_Click(object sender, EventArgs e)
        {
            var searchString = txtSearchFor.Text.Trim() == string.Empty ? null : txtSearchFor.Text;
            try
            {
               await Search(DateTime.SpecifyKind(datStartTime.Value, DateTimeKind.Local), DateTime.SpecifyKind(datEndTime.Value, DateTimeKind.Local) , chkMyBookmarksOnly.Checked, searchString);
            }
            catch (Exception ex)
            {
                Logger.Log.Error(null, ex);
            }
        }

        private async Task Search(DateTime startLocalTime, DateTime endLocalTime, bool myOwnBookmarksOnly, string searchString)
        {
            lsvBookmarks.Items.Clear();
            lblMessage.Text = $"Total Bookmarks found:";

            var items = new Item[] { selectedCameraItem };
            var kinds = new Guid[] { Kind.Camera, Kind.Microphone, Kind.Speaker };
    
            var searcher = new BookmarksFinder(items, kinds, myOwnBookmarksOnly, searchString);
            var bookmarks = await searcher.Search(startLocalTime, endLocalTime, bookmarksCount);

            AddToListView(bookmarks);
            OpenALPRBackgroundPlugin.Bookmarks = bookmarks;
        }

        private void AddToListView(Bookmark[]  bookmarks)
        {
            for (int i = 0; i < bookmarks.Length; i++)
            {
                var bookmark = bookmarks[i];

                //if (bookmark.Reference != OpenALPRBackgroundPlugin.openalprRefrence)
                //    continue;

                string[] row = new string[]
                                    {
                                        (i+1).ToString(),
                                        bookmark.TimeBegin.ToLocalTime().ToString (),
                                        bookmark.TimeEnd.ToLocalTime().ToString (),
                                        bookmark.Header,
                                        bookmark.Description
                                    };
                var listViewItem = new ListViewItem(row)
                {
                    Tag = bookmark.BookmarkFQID
                };

                lsvBookmarks.Items.Add(listViewItem);

                if (OpenALPRBackgroundPlugin.Stop)
                    break;
            }

            btnNext.Enabled = bookmarks.Length == bookmarksCount;
            lblMessage.Text = $"Total Bookmarks found: {bookmarks.Length.ToString()}";
        }

        private async void BtnNext_Click(object sender, EventArgs e)
        {
            if (lsvBookmarks.Items.Count != 0)
            {
                var lsvItem = lsvBookmarks.Items[lsvBookmarks.Items.Count - 1];
                if (lsvItem != null && lsvItem.Tag != null)
                {
                    var bookmarkFQID = lsvItem.Tag as FQID;
                    var searchString = txtSearchFor.Text.Trim() == string.Empty ? null : txtSearchFor.Text;
                    try
                    {
                        var startTime = DateTime.Now;
                        DateTime.TryParse(lsvItem.SubItems[1].Text, out startTime);
                        await Next(bookmarkFQID, DateTime.SpecifyKind(startTime, DateTimeKind.Local), DateTime.SpecifyKind(datEndTime.Value, DateTimeKind.Local), chkMyBookmarksOnly.Checked, searchString);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Error(null, ex);
                    }
                }
            }
        }

        private async Task Next(FQID bookmarkFQID, DateTime startLocalTime, DateTime endLocalTime, bool myOwnBookmarksOnly, string searchString)
        {
            lsvBookmarks.Items.Clear();
            lblMessage.Text = $"Total Bookmarks found:";

            var items = new Item[] { selectedCameraItem };
            var kinds = new Guid[] { Kind.Camera, Kind.Microphone, Kind.Speaker };

            var searcher = new BookmarksFinder(items, kinds, myOwnBookmarksOnly, searchString);
            var bookmarks = await searcher.Next(bookmarkFQID, startLocalTime, DateTime.SpecifyKind(endLocalTime, DateTimeKind.Local), bookmarksCount);

            AddToListView(bookmarks);
            OpenALPRBackgroundPlugin.Bookmarks = bookmarks;
        }

        private void ButtonCameraSelect_Click(object sender, EventArgs e)
        {
            var form = new ItemPickerForm
            {
                CategoryFilter = Category.VideoIn,
                AutoAccept = true
            };

            form.Init(Configuration.Instance.GetItemsByKind(Kind.Camera));
           
            if (form.ShowDialog() == DialogResult.OK)
            {
                txtCameraName.Text = string.Empty;
                selectedCameraItem = form.SelectedItem;
                if (selectedCameraItem != null)
                {
                    txtCameraName.Text = selectedCameraItem.Name;

                    try
                    {
                        Settings1.Default.usedFQID = selectedCameraItem.FQID.ObjectId;
                        Settings1.Default.Save();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Error(null, ex);
                    }
                }
            }
        }

        private async void EditBookmark(object sender, EventArgs e)
        {
            if (lsvBookmarks.SelectedItems.Count != 0)
            {
                var item = lsvBookmarks.SelectedItems[0];
                if (item != null && item.Tag != null)
                {
                    using (var edit = new EditBookmark())
                    {
                        edit.BeginTime = item.SubItems[1].Text;
                        edit.EndTime = item.SubItems[2].Text;
                        edit.Reference = item.SubItems[3].Text;
                        edit.Header = item.SubItems[4].Text;
                        edit.Description = item.SubItems[5].Text;

                        edit.ShowDialog(this);
                        if (edit.saved)
                        {
                            if (await BookmarksFinder.UpdateBookmark(item.Tag as FQID, edit.Reference, edit.Header, edit.Description))
                            {
                                item.SubItems[3].Text = edit.Reference;
                                item.SubItems[4].Text = edit.Header;
                                item.SubItems[5].Text = edit.Description;
                            }
                        }
                    }
                }
            }
        }

        private async void DeleteBookmark(object sender, EventArgs e)
        {
            if (lsvBookmarks.SelectedItems.Count != 0)
            {
                var item = lsvBookmarks.SelectedItems[0];
                if (item != null && item.Tag != null)
                {
                    var delete = MessageBox.Show($"Are you sure you want to delete '{item.SubItems[3].Text}' '{item.SubItems[4].Text}'  ?", "Deleting Bookmark", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if(delete == DialogResult.Yes)
                    {
                        if(await BookmarksFinder.DeleteBookmark(item.Tag as FQID))
                            lsvBookmarks.Items.Remove(item);
                    }
                }
            }
        }

        private async void ViewData(object sender, EventArgs e)
        {
            if (lsvBookmarks.SelectedItems.Count != 0)
            {
                var item = lsvBookmarks.SelectedItems[0];
                if (item != null && item.Tag != null)
                {
                    var bookmark = await BookmarksFinder.GetBookmark(item.Tag as FQID);
                    if (bookmark != null)
                    {
                        VideoReplayView videoReplayView = null;
                        try
                        {
                            videoReplayView = new VideoReplayView();
                            var tuple = new Tuple<Item, DateTime, DateTime>(bookmark.GetDeviceItem(), bookmark.TimeBegin, bookmark.TimeEnd);
                            videoReplayView.Add(new Tuple<Item, DateTime, DateTime>[] { tuple });
                            videoReplayView.ShowDialog(this);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log.Error(null, ex);
                        }
                        finally
                        {
                            if (videoReplayView != null)
                                videoReplayView.CloseMe();
                        }
                    }
                }
            }
        }

        private void LsvBookmarks_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                try
                {
                    var node = sender as ListView;
                    node.ContextMenu = cmBookmarks;

                    cmBookmarks.MenuItems[0].Enabled = true;
                    cmBookmarks.MenuItems[1].Enabled = true;

                    cmBookmarks.Show(node, new System.Drawing.Point(e.X, e.Y));
                }
                catch (Exception ex)
                {
                    Logger.Log.Error(null, ex);
                }
            }
        }

        private async Task CreateBookmark()
        {
            try
            {
                for (int i = 0; i < 120; i++)
                {
                    var time = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local);
                    await BookmarksFinder.CreateBookmark(
                                      selectedCameraItem.FQID,
                                      time.AddSeconds(-5),
                                      time,
                                      $"ref: {i.ToString()}",
                                      $"header: {i.ToString()}",
                                      $"desc: {i.ToString()}");

                }
            }
            catch (Exception ex)
            {
                Logger.Log.Error(null, ex);
            }
        }

        private async void BtnMapCameras_Click(object sender, EventArgs e)
        {
            IDictionary<string, OpenALPRCameraName> dictionary = new Dictionary<string, OpenALPRCameraName>();

            try
            {
                var lines = GetCameraMapping();
                for(int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if(!string.IsNullOrEmpty (line))
                    {
                        var entry = line.Split(new char[] { '|' });
                        var milestoneCameraName = string.Empty;
                        var alprCameraName = string.Empty;
                        var alprCameraId = string.Empty;

                        if (entry.Length != 0)
                            milestoneCameraName = entry[0];

                        if (entry.Length > 1)
                            alprCameraName = entry[1];

                        if (entry.Length > 2)
                            alprCameraId = entry[2];

                        if (milestoneCameraName.Length != 0)
                            dictionary.Add(milestoneCameraName, new OpenALPRCameraName { OpenALPRname = alprCameraName, OpenALPRid = alprCameraId });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Error(null, ex);
            }

            using (var cameraMapping = new CameraMapping())
            {
                try
                {
                    cameraMapping.dictionary = dictionary;
                    cameraMapping.ShowDialog(this);
                    if (cameraMapping.Saved)
                    {
                        dictionary = cameraMapping.dictionary;
                        var filePath = CameraMappingFile();
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            using (var outputFile = new StreamWriter(filePath))
                                foreach (var item in dictionary)
                                {
                                    await outputFile.WriteLineAsync($"{item.Key}|{item.Value.OpenALPRname}|{item.Value.OpenALPRid}");
                                }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log.Error(null, ex);
                }
            }
        }

        private string[] GetCameraMapping()
        {
            var filePath = CameraMappingFile();
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                return File.ReadAllLines(filePath);

            return new string[0];
        }
       
        private string CameraMappingFile()
        {
            var mappingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), OpenALPRPluginDefinition.PlugName, "Mapping");

            if (!Directory.Exists(mappingPath))
            {
                Directory.CreateDirectory(mappingPath);
                Helper.SetDirectoryNetworkServiceAccessControl(mappingPath);
            }

            return Path.Combine(mappingPath, "CameraMapping.txt");
        }

        private void BtnHelp_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start("http://doc.openalpr.com/");
            }
            catch (Exception ex)
            {
                Logger.Log.Error(null, ex);
            }
        }

        private void BtnOpenLogLocation_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", @"C:\ProgramData\OpenALPR\Log");

            }
            catch (Exception ex)
            {
                Logger.Log.Error(null, ex);
            }
        }
    }
}