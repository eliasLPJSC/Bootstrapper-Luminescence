using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using System.Collections.Generic;
using System.Drawing;

using FormPanel = System.Windows.Forms.Panel;
using FormLabel = System.Windows.Forms.Label;
using FormMessageBox = System.Windows.Forms.MessageBox;
using FormApplication = System.Windows.Forms.Application;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;

namespace Luminescence_v1._03E
{
    // =========================================================================
    // 1. FORM1 MUST BE THE FIRST CLASS IN THE NAMESPACE FOR THE DESIGNER TO WORK
    // =========================================================================
    public partial class Form1 : Form
    {
        private FormPanel rightSpacerPanel;
        private string configFilePath;
        private string tabsFilePath;
        private AppSettings currentSettings;
        private EditorSessionState currentSessionState;

        private Guna.UI2.WinForms.Guna2Button btnFilterMenuToggle;
        private Guna.UI2.WinForms.Guna2Panel filterDropdownPanel;
        private Guna.UI2.WinForms.Guna2CheckBox chkVerified;
        private Guna.UI2.WinForms.Guna2CheckBox chkUniversal;
        private Guna.UI2.WinForms.Guna2CheckBox chkPatched;
        private Guna.UI2.WinForms.Guna2ComboBox cmbKeySystemFilter;
        private Guna.UI2.WinForms.Guna2ComboBox cmbSortBy;
        private bool isFilterMenuOpen = false;

        private int currentInfinitePage = 1;
        private bool isLoadingMoreScripts = false;
        private bool hasMorePages = true;
        private FormLabel loadingIndicatorLabel;
        private System.Windows.Forms.Timer scrollDebounceTimer;

        // Full Screen State Variables
        private bool isFullScreen = false;
        private FormWindowState originalWindowState = FormWindowState.Normal;
        private FormBorderStyle originalFormBorderStyle = FormBorderStyle.Sizable;
        private Rectangle originalBounds;

        public Form1()
        {
            InitializeComponent();

            if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            {
                // Isolate save paths to the current user's AppData folder so they don't overwrite each other
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Luminescence");
                Directory.CreateDirectory(appDataDir);

                configFilePath = Path.Combine(appDataDir, "config.json");
                tabsFilePath = Path.Combine(appDataDir, "tabs.json");

                LoadAppSettings();
                LoadEditorSession();
                InitializeWebView();
            }

            this.FormClosing += Form1_FormClosing;
        }

        private void ToggleFullScreen()
        {
            if (!isFullScreen)
            {
                originalWindowState = this.WindowState;
                originalFormBorderStyle = this.FormBorderStyle;
                originalBounds = this.Bounds;

                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
                isFullScreen = true;
            }
            else
            {
                this.FormBorderStyle = originalFormBorderStyle;
                this.WindowState = originalWindowState;
                if (originalWindowState == FormWindowState.Normal)
                {
                    this.Bounds = originalBounds;
                }
                isFullScreen = false;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11)
            {
                ToggleFullScreen();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadAppSettings()
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    string json = File.ReadAllText(configFilePath);
                    currentSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    currentSettings = new AppSettings();
                }
            }
            catch
            {
                currentSettings = new AppSettings();
            }
        }

        private void SaveAppSettings()
        {
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return;
            try
            {
                if (toggleAlwaysOnTop != null) currentSettings.AlwaysOnTop = toggleAlwaysOnTop.Checked;
                if (toggleUnlockFps != null) currentSettings.UnlockFps = toggleUnlockFps.Checked;

                string json = JsonSerializer.Serialize(currentSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFilePath, json);
            }
            catch { }
        }

        private void LoadEditorSession()
        {
            try
            {
                if (File.Exists(tabsFilePath))
                {
                    string json = File.ReadAllText(tabsFilePath);
                    currentSessionState = JsonSerializer.Deserialize<EditorSessionState>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? GetDefaultSession();
                }
                else
                {
                    currentSessionState = GetDefaultSession();
                }
            }
            catch
            {
                currentSessionState = GetDefaultSession();
            }
        }

        private EditorSessionState GetDefaultSession()
        {
            var state = new EditorSessionState();
            state.ActiveTabId = "tab_default";
            state.Tabs.Add(new EditorTabItem
            {
                Id = "tab_default",
                Title = "Tab 1",
                Content = "print(\"Hello, Luminescence!\")"
            });
            return state;
        }

        private void SaveEditorSession()
        {
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return;
            try
            {
                string json = JsonSerializer.Serialize(currentSessionState, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(tabsFilePath, json);
            }
            catch { }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveAppSettings();
            SaveEditorSession();
        }

        private async void InitializeWebView()
        {
            if (webView21 == null) return;
            await webView21.EnsureCoreWebView2Async(null);

            webView21.CoreWebView2.Settings.AreDevToolsEnabled = true;
            webView21.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            webView21.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            string sessionJson = JsonSerializer.Serialize(currentSessionState, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string monacoHtml = GetMonacoHtml(sessionJson);
            webView21.NavigateToString(monacoHtml);
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = e.TryGetWebMessageAsString();
                if (!string.IsNullOrEmpty(message))
                {
                    var updatedSession = JsonSerializer.Deserialize<EditorSessionState>(message, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (updatedSession != null)
                    {
                        currentSessionState = updatedSession;
                        SaveEditorSession();
                    }
                }
            }
            catch { }
        }

        private string GetMonacoHtml(string initialSessionJson)
        {
            return @"<!DOCTYPE html>
<html>
<head>
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"" />
    <meta http-equiv=""Content-Type"" content=""text/html;charset=utf-8"" >
    <style>
        * { box-sizing: border-box; }
        html, body { height: 100%; margin: 0; padding: 0; background-color: #000000; font-family: 'Segoe UI', sans-serif; overflow: hidden; user-select: none; }
        #wrapper { display: flex; flex-direction: column; height: 100vh; position: relative; }
        
        #tab-bar { 
            display: flex; 
            background: #000000; 
            height: 38px; 
            align-items: center; 
            padding: 0 8px;
            gap: 4px;
        }

        .nav-btn {
            background: #080808;
            color: #888888;
            border: 1px solid #121212;
            height: 24px;
            width: 22px;
            border-radius: 3px;
            cursor: pointer;
            font-size: 13px;
            display: none;
            align-items: center;
            justify-content: center;
            user-select: none;
            flex-shrink: 0;
            line-height: 1;
        }
        .nav-btn:hover { color: #ffffff; background: #141414; border-color: #222222; }

        #tabs-list { 
            display: flex; 
            flex: 1; 
            overflow-x: auto; 
            scrollbar-width: none; 
            white-space: nowrap; 
            scroll-behavior: smooth;
            align-items: center;
            height: 100%;
        }
        #tabs-list::-webkit-scrollbar { display: none; }
        
        .tab { 
            background: #080808; 
            color: #777777; 
            padding: 4px 10px; 
            margin-right: 4px; 
            border-radius: 3px; 
            font-size: 12px; 
            cursor: pointer; 
            user-select: none; 
            display: inline-flex; 
            align-items: center; 
            gap: 8px;
            border: 1px solid #121212;
            flex-shrink: 0;
            height: 26px;
            opacity: 1;
            transform: scale(1);
            transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
        }
        .tab:hover { background: #101010; color: #aaaaaa; }
        .tab.active { background: #141414; color: #ffffff; border-color: #222222; }
        
        .tab.closing {
            opacity: 0;
            transform: scale(0.85);
            max-width: 0;
            padding-left: 0;
            padding-right: 0;
            margin-right: 0;
            border: none;
        }
        
        .tab .close-btn { color: #555; font-weight: bold; border-radius: 3px; padding: 0 3px; transition: color 0.15s; }
        .tab .close-btn:hover { color: #ff5555; background: #222; }
        
        #add-tab-btn { 
            background: #080808; 
            color: #888888; 
            border: 1px solid #121212; 
            height: 24px; 
            width: 26px; 
            border-radius: 3px; 
            cursor: pointer; 
            font-weight: bold; 
            flex-shrink: 0;
            display: flex;
            align-items: center;
            justify-content: center;
            margin-left: 2px;
        }
        #add-tab-btn:hover { background: #141414; color: #ffffff; border-color: #222222; }
        
        #container { flex: 1; }

        #tab-context-menu {
            display: none;
            position: absolute;
            z-index: 10000;
            background: #121212;
            border: 1px solid #282828;
            border-radius: 4px;
            padding: 4px 0;
            box-shadow: 0 4px 12px rgba(0,0,0,0.5);
            min-width: 110px;
        }
        .context-item {
            padding: 6px 12px;
            font-size: 12px;
            color: #cccccc;
            cursor: pointer;
            transition: background 0.15s;
        }
        .context-item:hover {
            background: #222222;
            color: #ffffff;
        }
        .context-item.danger:hover {
            background: #3a1414;
            color: #ff5555;
        }

        #modal-overlay {
            display: none;
            position: absolute;
            top: 0; left: 0; width: 100%; height: 100%;
            background: rgba(0, 0, 0, 0.6);
            z-index: 20000;
            align-items: center;
            justify-content: center;
        }

        #rename-modal {
            background: #121212;
            border: 1px solid #282828;
            border-radius: 6px;
            width: 280px;
            padding: 16px;
            box-shadow: 0 8px 24px rgba(0,0,0,0.8);
            display: flex;
            flex-direction: column;
            gap: 12px;
        }

        #rename-modal h3 {
            margin: 0;
            font-size: 13px;
            color: #ffffff;
            font-weight: 600;
        }

        #rename-input {
            background: #080808;
            border: 1px solid #222222;
            color: #ffffff;
            padding: 7px 10px;
            border-radius: 4px;
            font-size: 12px;
            outline: none;
            width: 100%;
        }
        #rename-input:focus { border-color: #444444; }

        #rename-actions {
            display: flex;
            justify-content: flex-end;
            gap: 6px;
        }

        .modal-btn {
            background: #181818;
            color: #cccccc;
            border: 1px solid #282828;
            padding: 5px 12px;
            border-radius: 4px;
            font-size: 11px;
            cursor: pointer;
            font-weight: 600;
        }
        .modal-btn:hover { background: #222222; color: #ffffff; }
        .modal-btn.primary { background: #e03c3c; border-color: #ff4444; color: #ffffff; }
        .modal-btn.primary:hover { background: #ff4444; }

        #toast-container {
            position: absolute;
            bottom: 20px;
            right: 20px;
            display: flex;
            flex-direction: column-reverse;
            gap: 8px;
            z-index: 999;
            pointer-events: none;
        }

        .toast-item {
            background: rgba(18, 18, 18, 0.95);
            color: #ffffff;
            border: 1px solid #282828;
            border-left: 3px solid #ff4444;
            padding: 10px 16px;
            border-radius: 4px;
            font-size: 13px;
            font-weight: 600;
            box-shadow: 0 6px 16px rgba(0,0,0,0.8);
            opacity: 0;
            transform: translateY(15px) scale(0.95);
            transition: all 0.25s cubic-bezier(0.175, 0.885, 0.32, 1.275);
        }

        .toast-item.show { opacity: 1; transform: translateY(0) scale(1); }
        .toast-item.hide { opacity: 0; transform: translateY(-10px) scale(0.9); }
    </style>
</head>
<body>
    <div id=""wrapper"">
        <div id=""tab-bar"">
            <button id=""scroll-left"" class=""nav-btn"" onclick=""scrollTabs(-140)"">‹</button>
            <div id=""tabs-list""></div>
            <button id=""scroll-right"" class=""nav-btn"" onclick=""scrollTabs(140)"">›</button>
            <button id=""add-tab-btn"" onclick=""addNewTab()"">+</button>
        </div>
        <div id=""container""></div>
        <div id=""toast-container""></div>
    </div>

    <div id=""tab-context-menu"">
        <div class=""context-item"" onclick=""openRenameModal()"">Rename</div>
        <div class=""context-item danger"" onclick=""closeTargetTab()"">Close</div>
    </div>

    <div id=""modal-overlay"">
        <div id=""rename-modal"">
            <h3>Rename Tab</h3>
            <input type=""text"" id=""rename-input"" placeholder=""Enter new tab name..."" />
            <div id=""rename-actions"">
                <button class=""modal-btn"" onclick=""closeRenameModal()"">Cancel</button>
                <button class=""modal-btn primary"" onclick=""submitRename()"">Save</button>
            </div>
        </div>
    </div>

    <script src=""https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.38.0/min/vs/loader.min.js""></script>
    <script>
        let editor;
        let tabs = [];
        let activeTabId = null;
        let rightClickedTabId = null;
        const MAX_TABS = 30;
        const MAX_TOASTS = 5;

        const initialSession = " + initialSessionJson + @";

        document.addEventListener('contextmenu', function(e) {
            e.preventDefault();
        });

        document.addEventListener('keydown', function(e) {
            if (
                e.keyCode === 123 ||
                (e.ctrlKey && e.shiftKey && (e.keyCode === 73 || e.keyCode === 74 || e.keyCode === 67)) ||
                (e.ctrlKey && e.keyCode === 85)
            ) {
                e.preventDefault();
                return false;
            }

            if (document.getElementById('modal-overlay').style.display === 'flex') {
                if (e.keyCode === 13) {
                    submitRename();
                } else if (e.keyCode === 27) {
                    closeRenameModal();
                }
            }
        });

        document.addEventListener('click', function(e) {
            if (!e.target.closest('#tab-context-menu')) {
                document.getElementById('tab-context-menu').style.display = 'none';
            }
        });

        require.config({ paths: { 'vs': 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.38.0/min/vs' } });
        require(['vs/editor/editor.main'], function() {
            monaco.editor.defineTheme('luminescence-black', {
                base: 'vs-dark',
                inherit: true,
                rules: [],
                colors: {
                    'editor.background': '#000000',
                    'editorGutter.background': '#000000'
                }
            });

            editor = monaco.editor.create(document.getElementById('container'), {
                theme: 'luminescence-black',
                automaticLayout: true,
                minimap: { enabled: false },
                scrollBeyondLastLine: false,
                wordWrap: 'on',
                lineNumbers: 'on',
                contextmenu: false
            });

            editor.onDidChangeModelContent(function() {
                queueSyncToCSharp();
            });

            loadTabsFromInitialSession();
        });

        let syncTimer = null;
        function queueSyncToCSharp() {
            clearTimeout(syncTimer);
            syncTimer = setTimeout(syncToCSharp, 300);
        }

        function syncToCSharp() {
            try {
                if (window.chrome && window.chrome.webview) {
                    const dataToSave = {
                        activeTabId: activeTabId,
                        tabs: tabs.map(t => ({
                            id: t.id,
                            title: t.title,
                            content: t.model.getValue()
                        }))
                    };
                    window.chrome.webview.postMessage(JSON.stringify(dataToSave));
                }
            } catch (e) {}
        }

        function loadTabsFromInitialSession() {
            try {
                if (initialSession && initialSession.tabs && initialSession.tabs.length > 0) {
                    initialSession.tabs.forEach(item => {
                        const model = monaco.editor.createModel(item.content || '', 'lua');
                        tabs.push({ id: item.id, title: item.title, model: model });
                    });
                    
                    const targetActive = initialSession.tabs.some(t => t.id === initialSession.activeTabId) ? initialSession.activeTabId : initialSession.tabs[0].id;
                    renderTabs();
                    switchTab(targetActive);
                    return;
                }
            } catch(e) {}

            addNewTab('Tab 1', '');
        }

        function showLimitToast(msg) {
            const container = document.getElementById('toast-container');
            const message = msg || 'Maximum tab limit reached (30)';

            while (container.children.length >= MAX_TOASTS) {
                container.removeChild(container.lastElementChild);
            }

            const toast = document.createElement('div');
            toast.className = 'toast-item';
            toast.innerText = message;
            container.prepend(toast);

            requestAnimationFrame(() => { toast.classList.add('show'); });

            setTimeout(() => {
                toast.classList.remove('show');
                toast.classList.add('hide');
                setTimeout(() => {
                    if (toast.parentNode) toast.parentNode.removeChild(toast);
                }, 250);
            }, 2500);
        }

        function checkOverflow() {
            const list = document.getElementById('tabs-list');
            const leftBtn = document.getElementById('scroll-left');
            const rightBtn = document.getElementById('scroll-right');
            const hasOverflow = list.scrollWidth > list.clientWidth;
            leftBtn.style.display = hasOverflow ? 'flex' : 'none';
            rightBtn.style.display = hasOverflow ? 'flex' : 'none';
        }

        function scrollTabs(amount) {
            document.getElementById('tabs-list').scrollBy({ left: amount, behavior: 'smooth' });
        }

        function addNewTab(title, code) {
            if (tabs.length >= MAX_TABS) {
                showLimitToast('Maximum tab limit reached (30)');
                return;
            }

            const id = 'tab_' + Date.now() + '_' + Math.random().toString(36).substr(2, 5);
            const tabTitle = title || ('Tab ' + (tabs.length + 1));
            const scriptCode = (code !== undefined) ? code : '';

            const model = monaco.editor.createModel(scriptCode, 'lua');
            tabs.push({ id: id, title: tabTitle, model: model });

            renderTabs();
            switchTab(id);
            syncToCSharp();

            setTimeout(function() {
                const list = document.getElementById('tabs-list');
                list.scrollTo({ left: list.scrollWidth, behavior: 'smooth' });
            }, 50);
        }

        function switchTab(id) {
            const tab = tabs.find(function(t) { return t.id === id; });
            if (!tab) return;
            activeTabId = id;
            editor.setModel(tab.model);
            renderTabs();
            syncToCSharp();
        }

        function openTabContextMenu(e, id) {
            e.preventDefault();
            e.stopPropagation();
            rightClickedTabId = id;

            const menu = document.getElementById('tab-context-menu');
            menu.style.left = e.clientX + 'px';
            menu.style.top = e.clientY + 'px';
            menu.style.display = 'block';
        }

        function openRenameModal() {
            document.getElementById('tab-context-menu').style.display = 'none';
            if (!rightClickedTabId) return;

            const tab = tabs.find(t => t.id === rightClickedTabId);
            if (tab) {
                const input = document.getElementById('rename-input');
                input.value = tab.title;

                const overlay = document.getElementById('modal-overlay');
                overlay.style.display = 'flex';

                setTimeout(() => {
                    input.focus();
                    input.select();
                }, 50);
            }
        }

        function closeRenameModal() {
            document.getElementById('modal-overlay').style.display = 'none';
        }

        function submitRename() {
            const newTitle = document.getElementById('rename-input').value.trim();
            if (newTitle && rightClickedTabId) {
                const tab = tabs.find(t => t.id === rightClickedTabId);
                if (tab) {
                    tab.title = newTitle;
                    renderTabs();
                    syncToCSharp();
                }
            }
            closeRenameModal();
        }

        function closeTargetTab() {
            if (rightClickedTabId) {
                closeTab(rightClickedTabId);
            }
            document.getElementById('tab-context-menu').style.display = 'none';
        }

        function closeTab(id, e) {
            if (e) e.stopPropagation();
            if (tabs.length <= 1) return;

            const tabElem = document.getElementById('element_' + id);
            if (tabElem) {
                tabElem.classList.add('closing');
                setTimeout(() => {
                    const idx = tabs.findIndex(function(t) { return t.id === id; });
                    if (idx !== -1) {
                        tabs[idx].model.dispose();
                        tabs.splice(idx, 1);

                        if (activeTabId === id) {
                            const nextTab = tabs[Math.max(0, idx - 1)];
                            switchTab(nextTab.id);
                        } else {
                            renderTabs();
                            syncToCSharp();
                        }
                    }
                }, 180);
            }
        }

        function renderTabs() {
            const container = document.getElementById('tabs-list');
            container.innerHTML = '';

            tabs.forEach(function(tab) {
                const div = document.createElement('div');
                div.id = 'element_' + tab.id;
                div.className = 'tab ' + (tab.id === activeTabId ? 'active' : '');
                div.onclick = function() { switchTab(tab.id); };
                div.oncontextmenu = function(e) { openTabContextMenu(e, tab.id); };

                div.innerHTML = '<span>' + tab.title + '</span><span class=""close-btn"" onclick=""closeTab(\'' + tab.id + '\', event)"">×</span>';
                container.appendChild(div);
            });

            checkOverflow();
        }

        function setEditorValue(val) {
            if (editor) editor.setValue(val);
        }

        window.addEventListener('resize', checkOverflow);

        document.getElementById('tabs-list').addEventListener('wheel', function(e) {
            if (e.deltaY !== 0) {
                this.scrollLeft += e.deltaY;
                e.preventDefault();
            }
        });
    </script>
</body>
</html>";
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return;

            if (webView21 != null) webView21.Visible = true;
            if (bottomActionPanel != null) bottomActionPanel.Visible = true;
            if (scriptHubPanel != null) scriptHubPanel.Visible = false;
            if (settingsPanel != null) settingsPanel.Visible = false;

            if (currentSettings != null)
            {
                if (toggleAlwaysOnTop != null)
                {
                    toggleAlwaysOnTop.Checked = currentSettings.AlwaysOnTop;
                }
                if (toggleUnlockFps != null)
                {
                    toggleUnlockFps.Checked = currentSettings.UnlockFps;
                }
                this.TopMost = currentSettings.AlwaysOnTop;
            }

            if (flowScriptContainer != null)
            {
                flowScriptContainer.FlowDirection = FlowDirection.LeftToRight;
                flowScriptContainer.WrapContents = true;
                flowScriptContainer.AutoScroll = true;
                flowScriptContainer.HorizontalScroll.Visible = false;
                flowScriptContainer.HorizontalScroll.Enabled = false;
                flowScriptContainer.SizeChanged += FlowScriptContainer_SizeChanged;
            }

            InitializeAdvancedFilterMenu();
            InitializeInfiniteScrollEvents();

            this.MouseWheel += (s, ev) =>
            {
                if (scriptHubPanel != null && scriptHubPanel.Visible)
                {
                    FlowScriptContainer_ScrollForInfiniteScroll(s, ev);
                }
            };

            if (rightSpacerPanel == null && webView21 != null)
            {
                rightSpacerPanel = new FormPanel();
                rightSpacerPanel.BackColor = DrawingColor.FromArgb(0, 0, 0);
                rightSpacerPanel.Width = 16;

                rightSpacerPanel.Top = webView21.Top + 33;
                rightSpacerPanel.Left = webView21.Right - 16;
                rightSpacerPanel.Height = webView21.Height - 33;

                rightSpacerPanel.Anchor = webView21.Anchor;
                this.Controls.Add(rightSpacerPanel);
                rightSpacerPanel.BringToFront();
            }
        }

        private void FlowScriptContainer_SizeChanged(object sender, EventArgs e)
        {
            RecalculateCardWidths();
        }

        private void RecalculateCardWidths()
        {
            if (flowScriptContainer == null) return;

            int netWidth = flowScriptContainer.Width - SystemInformation.VerticalScrollBarWidth - 24;
            int gapBetweenCards = 10;

            int targetCardWidth = Math.Max(160, (netWidth - gapBetweenCards - 12) / 2);

            int totalGridWidth = (targetCardWidth * 2) + gapBetweenCards;
            int horizontalPadding = Math.Max(4, (netWidth - totalGridWidth) / 2);

            flowScriptContainer.SuspendLayout();
            flowScriptContainer.Padding = new Padding(horizontalPadding, 10, horizontalPadding, 10);

            if (flowScriptContainer.Controls.Count > 0)
            {
                foreach (Control ctrl in flowScriptContainer.Controls)
                {
                    if (ctrl is Guna.UI2.WinForms.Guna2Panel card)
                    {
                        card.Width = targetCardWidth;
                        card.Margin = new Padding(gapBetweenCards / 2, 0, gapBetweenCards / 2, 12);
                        int innerWidth = targetCardWidth - 24;

                        int btnCopyWidth = 60;
                        int btnOpenWidth = 64;
                        int btnExecuteWidth = Math.Max(50, innerWidth - (btnCopyWidth + btnOpenWidth + 12));

                        foreach (Control child in card.Controls)
                        {
                            if (child is PictureBox pic)
                            {
                                pic.Width = innerWidth;
                            }
                            else if (child is FormLabel lbl && lbl.Height == 42)
                            {
                                lbl.Width = innerWidth;
                            }
                            else if (child is Guna.UI2.WinForms.Guna2Panel pill)
                            {
                                pill.Width = innerWidth;
                            }
                            else if (child is Guna.UI2.WinForms.Guna2Button btn)
                            {
                                if (btn.Text == "Copy")
                                {
                                    btn.Width = btnCopyWidth;
                                }
                                else if (btn.Text == "Open")
                                {
                                    btn.Width = btnOpenWidth;
                                    btn.Left = 12 + btnCopyWidth + 6;
                                }
                                else if (btn.Text == "Execute")
                                {
                                    btn.Width = btnExecuteWidth;
                                    btn.Left = 12 + btnCopyWidth + btnOpenWidth + 12;
                                }
                            }
                            else if (child is FormLabel badgeLbl && (badgeLbl.Text == "✔ VERIFIED" || badgeLbl.Text == "UNIVERSAL"))
                            {
                                badgeLbl.Left = Math.Max(85, targetCardWidth - 85);
                            }
                        }
                    }
                    else if (ctrl == loadingIndicatorLabel)
                    {
                        ctrl.Width = flowScriptContainer.Width - 40;
                    }
                }
            }
            flowScriptContainer.ResumeLayout();
        }

        private void InitializeAdvancedFilterMenu()
        {
            if (txtSearch == null) return;
            if (btnFilterMenuToggle == null)
            {
                btnFilterMenuToggle = new Guna.UI2.WinForms.Guna2Button();
                btnFilterMenuToggle.Size = new DrawingSize(40, txtSearch.Height);
                btnFilterMenuToggle.Location = new DrawingPoint(txtSearch.Left - 48, txtSearch.Top);
                btnFilterMenuToggle.FillColor = DrawingColor.FromArgb(18, 18, 18);
                btnFilterMenuToggle.ForeColor = DrawingColor.DarkGray;
                btnFilterMenuToggle.BorderColor = DrawingColor.FromArgb(35, 35, 35);
                btnFilterMenuToggle.BorderRadius = 6;
                btnFilterMenuToggle.BorderThickness = 1;
                btnFilterMenuToggle.Text = "⚙";
                btnFilterMenuToggle.Font = new DrawingFont("Segoe UI", 11F);
                btnFilterMenuToggle.Click += BtnFilterMenuToggle_Click;

                if (scriptHubPanel != null)
                {
                    scriptHubPanel.Controls.Add(btnFilterMenuToggle);
                    btnFilterMenuToggle.BringToFront();
                }
            }

            if (filterDropdownPanel == null)
            {
                filterDropdownPanel = new Guna.UI2.WinForms.Guna2Panel();
                filterDropdownPanel.Size = new DrawingSize(275, 275);
                filterDropdownPanel.Location = new DrawingPoint(btnFilterMenuToggle.Left, btnFilterMenuToggle.Bottom + 6);
                filterDropdownPanel.FillColor = DrawingColor.Transparent;
                filterDropdownPanel.BackColor = DrawingColor.Transparent;
                filterDropdownPanel.BorderThickness = 0;
                filterDropdownPanel.Visible = false;

                int yOffset = 14;

                FormLabel lblFilterHeader = new FormLabel();
                lblFilterHeader.Text = "FILTER";
                lblFilterHeader.Font = new DrawingFont("Segoe UI", 9F, DrawingFontStyle.Bold);
                lblFilterHeader.ForeColor = DrawingColor.DarkGray;
                lblFilterHeader.BackColor = DrawingColor.Transparent;
                lblFilterHeader.Location = new DrawingPoint(16, yOffset);
                lblFilterHeader.AutoSize = true;
                filterDropdownPanel.Controls.Add(lblFilterHeader);
                yOffset += 26;

                chkVerified = CreateFilterCheckbox("Verified", 16, yOffset);
                yOffset += 32;

                chkUniversal = CreateFilterCheckbox("Universal", 16, yOffset);
                yOffset += 32;

                chkPatched = CreateFilterCheckbox("Patched", 16, yOffset);
                yOffset += 36;

                FormLabel lblKey = new FormLabel();
                lblKey.Text = "Key system";
                lblKey.Font = new DrawingFont("Segoe UI", 8.5F);
                lblKey.ForeColor = DrawingColor.LightGray;
                lblKey.BackColor = DrawingColor.Transparent;
                lblKey.Location = new DrawingPoint(16, yOffset);
                lblKey.AutoSize = true;
                filterDropdownPanel.Controls.Add(lblKey);

                cmbKeySystemFilter = new Guna.UI2.WinForms.Guna2ComboBox();
                cmbKeySystemFilter.Size = new DrawingSize(140, 28);
                cmbKeySystemFilter.Location = new DrawingPoint(115, yOffset - 3);
                cmbKeySystemFilter.FillColor = DrawingColor.FromArgb(22, 22, 22);
                cmbKeySystemFilter.ForeColor = DrawingColor.White;
                cmbKeySystemFilter.BorderColor = DrawingColor.FromArgb(38, 38, 38);
                cmbKeySystemFilter.BorderRadius = 6;
                cmbKeySystemFilter.Font = new DrawingFont("Segoe UI", 8F);
                cmbKeySystemFilter.Items.AddRange(new object[] { "Any", "Yes", "No" });
                cmbKeySystemFilter.SelectedIndex = 0;
                cmbKeySystemFilter.StartIndex = 0;
                cmbKeySystemFilter.SelectedIndexChanged += async (s, e) => { await ResetAndTriggerSearch(); };
                filterDropdownPanel.Controls.Add(cmbKeySystemFilter);
                yOffset += 42;

                FormLabel lblSortHeader = new FormLabel();
                lblSortHeader.Text = "SORT";
                lblSortHeader.Font = new DrawingFont("Segoe UI", 9F, DrawingFontStyle.Bold);
                lblSortHeader.ForeColor = DrawingColor.DarkGray;
                lblSortHeader.BackColor = DrawingColor.Transparent;
                lblSortHeader.Location = new DrawingPoint(16, yOffset);
                lblSortHeader.AutoSize = true;
                filterDropdownPanel.Controls.Add(lblSortHeader);
                yOffset += 24;

                cmbSortBy = new Guna.UI2.WinForms.Guna2ComboBox();
                cmbSortBy.Size = new DrawingSize(240, 28);
                cmbSortBy.Location = new DrawingPoint(16, yOffset);
                cmbSortBy.FillColor = DrawingColor.FromArgb(22, 22, 22);
                cmbSortBy.ForeColor = DrawingColor.White;
                cmbSortBy.BorderColor = DrawingColor.FromArgb(38, 38, 38);
                cmbSortBy.BorderRadius = 6;
                cmbSortBy.Font = new DrawingFont("Segoe UI", 8.5F);
                cmbSortBy.Items.AddRange(new object[] { "Update date", "Views", "Likes" });
                cmbSortBy.SelectedIndex = 0;
                cmbSortBy.StartIndex = 0;
                cmbSortBy.SelectedIndexChanged += async (s, e) => { await ResetAndTriggerSearch(); };
                filterDropdownPanel.Controls.Add(cmbSortBy);

                if (scriptHubPanel != null)
                {
                    scriptHubPanel.Controls.Add(filterDropdownPanel);
                    filterDropdownPanel.BringToFront();
                }
            }
        }

        private Guna.UI2.WinForms.Guna2CheckBox CreateFilterCheckbox(string text, int x, int y)
        {
            Guna.UI2.WinForms.Guna2CheckBox chk = new Guna.UI2.WinForms.Guna2CheckBox();
            chk.Text = text;
            chk.Font = new DrawingFont("Segoe UI", 9F);
            chk.ForeColor = DrawingColor.LightGray;
            chk.BackColor = DrawingColor.Transparent;
            chk.CheckedState.BorderColor = DrawingColor.FromArgb(230, 60, 60);
            chk.CheckedState.FillColor = DrawingColor.FromArgb(200, 40, 40);
            chk.UncheckedState.BorderColor = DrawingColor.FromArgb(50, 50, 50);
            chk.UncheckedState.FillColor = DrawingColor.Transparent;
            chk.Size = new DrawingSize(120, 22);
            chk.Location = new DrawingPoint(x, y);
            chk.CheckedChanged += async (s, e) => { await ResetAndTriggerSearch(); };
            if (filterDropdownPanel != null) filterDropdownPanel.Controls.Add(chk);
            return chk;
        }

        private void InitializeInfiniteScrollEvents()
        {
            if (flowScriptContainer != null)
            {
                if (scrollDebounceTimer == null)
                {
                    scrollDebounceTimer = new System.Windows.Forms.Timer();
                    scrollDebounceTimer.Interval = 200;
                    scrollDebounceTimer.Tick += ScrollDebounceTimer_Tick;
                }

                flowScriptContainer.Scroll -= FlowScriptContainer_ScrollForInfiniteScroll;
                flowScriptContainer.MouseWheel -= FlowScriptContainer_ScrollForInfiniteScroll;

                flowScriptContainer.Scroll += FlowScriptContainer_ScrollForInfiniteScroll;
                flowScriptContainer.MouseWheel += FlowScriptContainer_ScrollForInfiniteScroll;
            }
        }

        private void FlowScriptContainer_ScrollForInfiniteScroll(object sender, EventArgs e)
        {
            if (isLoadingMoreScripts || !hasMorePages || flowScriptContainer == null) return;

            if (flowScriptContainer.Controls.Count > 0)
            {
                Control lastControl = flowScriptContainer.Controls[flowScriptContainer.Controls.Count - 1];

                if (lastControl == loadingIndicatorLabel && flowScriptContainer.Controls.Count > 1)
                {
                    lastControl = flowScriptContainer.Controls[flowScriptContainer.Controls.Count - 2];
                }

                int lastControlBottom = lastControl.Bottom;
                int currentScrollBottom = flowScriptContainer.ClientSize.Height - flowScriptContainer.AutoScrollPosition.Y;

                if (currentScrollBottom >= (lastControlBottom - 300))
                {
                    scrollDebounceTimer.Stop();
                    scrollDebounceTimer.Start();
                }
            }
        }

        private async void ScrollDebounceTimer_Tick(object sender, EventArgs e)
        {
            scrollDebounceTimer.Stop();

            if (isLoadingMoreScripts || !hasMorePages) return;

            currentInfinitePage++;
            await TriggerScriptSearch(true);
        }

        private void BtnFilterMenuToggle_Click(object sender, EventArgs e)
        {
            isFilterMenuOpen = !isFilterMenuOpen;
            if (filterDropdownPanel != null) filterDropdownPanel.Visible = isFilterMenuOpen;
            if (isFilterMenuOpen)
            {
                if (filterDropdownPanel != null) filterDropdownPanel.BringToFront();
                if (btnFilterMenuToggle != null) btnFilterMenuToggle.ForeColor = DrawingColor.White;
            }
            else
            {
                if (btnFilterMenuToggle != null) btnFilterMenuToggle.ForeColor = DrawingColor.DarkGray;
            }
        }

        private async Task ResetAndTriggerSearch()
        {
            currentInfinitePage = 1;
            hasMorePages = true;
            if (flowScriptContainer != null) flowScriptContainer.Controls.Clear();
            await TriggerScriptSearch(false);
        }

        private async Task SetActiveTabScript(string scriptCode)
        {
            if (webView21 != null && webView21.CoreWebView2 != null)
            {
                string safeScript = scriptCode.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
                await webView21.CoreWebView2.ExecuteScriptAsync($"setEditorValue(\"{safeScript}\");");
            }
        }

        private void ShowLoadingLabel()
        {
            if (flowScriptContainer == null) return;
            if (loadingIndicatorLabel == null)
            {
                loadingIndicatorLabel = new FormLabel();
                loadingIndicatorLabel.Text = "Loading. .";
                loadingIndicatorLabel.Font = new DrawingFont("Segoe UI", 10F, DrawingFontStyle.Bold);
                loadingIndicatorLabel.ForeColor = DrawingColor.DarkGray;
                loadingIndicatorLabel.BackColor = DrawingColor.Transparent;
                loadingIndicatorLabel.TextAlign = ContentAlignment.MiddleCenter;
                loadingIndicatorLabel.Margin = new Padding(0, 15, 0, 20);
                loadingIndicatorLabel.Height = 30;
            }

            loadingIndicatorLabel.Width = flowScriptContainer.Width - 40;

            if (!flowScriptContainer.Controls.Contains(loadingIndicatorLabel))
            {
                flowScriptContainer.Controls.Add(loadingIndicatorLabel);
            }
            flowScriptContainer.Controls.SetChildIndex(loadingIndicatorLabel, flowScriptContainer.Controls.Count - 1);
        }

        private void HideLoadingLabel()
        {
            if (flowScriptContainer != null && loadingIndicatorLabel != null && flowScriptContainer.Controls.Contains(loadingIndicatorLabel))
            {
                flowScriptContainer.Controls.Remove(loadingIndicatorLabel);
            }
        }

        private void guna2Button2_Click(object sender, EventArgs e)
        {
            SaveAppSettings();
            SaveEditorSession();
            FormApplication.Exit();
        }

        private void guna2Button7_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
        }

        private void guna2Button1_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://discord.gg/tBzMH9Eaj7",
                UseShellExecute = true
            });
        }

        private async void guna2Button3_Click(object sender, EventArgs e)
        {
            try
            {
                QuorumAPI.QuorumModule.AttachAPI();

                string notificationScript = @"
                    task.spawn(function()
                        pcall(function()
                            if not game:IsLoaded() then
                                game.Loaded:Wait()
                            end
                            
                            local StarterGui = game:GetService('StarterGui')
                            local success = false
                            local attempts = 0
                            
                            while not success and attempts < 25 do
                                attempts = attempts + 1
                                success = pcall(function()
                                    StarterGui:SetCore('SendNotification', {
                                        Title = '[ Luminescence ]',
                                        Text = 'Successfully Injected',
                                        Duration = 5,
                                        Icon = 'rbxthumb://type=Asset&id=76675993626416&w=150&h=150'
                                    })
                                end)
                                if not success then
                                    task.wait(0.5)
                                end
                            end
                        end)
                    end);
                ";

                await Task.Delay(2000);
                QuorumAPI.QuorumModule.ExecuteScript(notificationScript);
            }
            catch (Exception ex)
            {
                FormMessageBox.Show("Failed to attach or execute notification: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void guna2Button8_Click(object sender, EventArgs e)
        {
            if (webView21 != null && webView21.CoreWebView2 != null)
            {
                try
                {
                    string resultJson = await webView21.CoreWebView2.ExecuteScriptAsync("editor.getValue();");
                    string script = System.Text.Json.JsonSerializer.Deserialize<string>(resultJson);

                    if (!string.IsNullOrEmpty(script))
                    {
                        QuorumAPI.QuorumModule.ExecuteScript(script);
                    }
                }
                catch (Exception ex)
                {
                    FormMessageBox.Show("Failed to execute script: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async void guna2Button6_Click(object sender, EventArgs e)
        {
            if (webView21 != null) webView21.Visible = false;
            if (settingsPanel != null) settingsPanel.Visible = false;
            if (bottomActionPanel != null) bottomActionPanel.Visible = false;
            if (scriptHubPanel != null) scriptHubPanel.Visible = true;

            if (flowScriptContainer != null && flowScriptContainer.Controls.Count == 0)
            {
                await ResetAndTriggerSearch();
            }
        }

        private void guna2Button9_Click(object sender, EventArgs e)
        {
            if (webView21 != null) webView21.Visible = false;
            if (scriptHubPanel != null) scriptHubPanel.Visible = false;
            if (bottomActionPanel != null) bottomActionPanel.Visible = false;
            if (settingsPanel != null) settingsPanel.Visible = true;
        }

        private void guna2Button10_Click(object sender, EventArgs e)
        {
            if (settingsPanel != null) settingsPanel.Visible = false;
            if (scriptHubPanel != null) scriptHubPanel.Visible = false;
            if (webView21 != null) webView21.Visible = true;
            if (bottomActionPanel != null) bottomActionPanel.Visible = true;
        }

        private void toggleAlwaysOnTop_CheckedChanged(object sender, EventArgs e)
        {
            if (toggleAlwaysOnTop != null) this.TopMost = toggleAlwaysOnTop.Checked;
            SaveAppSettings();
        }

        private void toggleUnlockFps_CheckedChanged(object sender, EventArgs e)
        {
            SaveAppSettings();
        }

        private void lblScriptHubTitle_Click(object sender, EventArgs e) { }

        private async void btnSearch_Click(object sender, EventArgs e)
        {
            await ResetAndTriggerSearch();
        }

        private async void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await ResetAndTriggerSearch();
            }
        }

        private async Task TriggerScriptSearch(bool append = false)
        {
            if (isLoadingMoreScripts || !hasMorePages) return;
            isLoadingMoreScripts = true;

            ShowLoadingLabel();
            if (flowScriptContainer != null) flowScriptContainer.Update();

            string query = txtSearch != null ? txtSearch.Text.Trim() : "";
            if (!append)
            {
                if (flowScriptContainer != null) flowScriptContainer.Controls.Clear();
                ShowLoadingLabel();
            }

            string sortBy = "createdAt";
            if (cmbSortBy != null)
            {
                string selectedSort = cmbSortBy.SelectedItem?.ToString();
                if (selectedSort == "Views") sortBy = "views";
                else if (selectedSort == "Likes") sortBy = "likeCount";
                else sortBy = "createdAt";
            }

            try
            {
                await Task.Delay(200);

                using (HttpClient client = new HttpClient())
                {
                    string url;
                    if (string.IsNullOrEmpty(query))
                    {
                        url = $"https://scriptblox.com/api/script/fetch?page={currentInfinitePage}&sortBy={sortBy}";
                    }
                    else
                    {
                        url = $"https://scriptblox.com/api/script/search?q={Uri.EscapeDataString(query)}&page={currentInfinitePage}&sortBy={sortBy}";
                    }

                    string jsonResponse = await client.GetStringAsync(url);

                    HideLoadingLabel();
                    ParseAndDisplayGridScripts(jsonResponse);
                }
            }
            catch (Exception ex)
            {
                HideLoadingLabel();
                FormMessageBox.Show("Failed to load scripts: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            isLoadingMoreScripts = false;
        }

        private void ParseAndDisplayGridScripts(string jsonResponse)
        {
            using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("result", out var result) && result.TryGetProperty("scripts", out var scripts))
                {
                    int count = 0;
                    foreach (var scriptObj in scripts.EnumerateArray())
                    {
                        count++;
                        string title = scriptObj.TryGetProperty("title", out var t) ? t.GetString() : "Unknown Script";
                        string scriptCode = scriptObj.TryGetProperty("script", out var s) ? s.GetString() : "";

                        string gameName = "Unknown Game";
                        string imageUrl = "";
                        if (scriptObj.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Object)
                        {
                            if (game.TryGetProperty("name", out var gn)) gameName = gn.GetString();
                            if (game.TryGetProperty("imageUrl", out var gi)) imageUrl = gi.GetString();
                        }

                        bool isVerified = scriptObj.TryGetProperty("verified", out var ver) && (ver.ValueKind == JsonValueKind.True || (ver.ValueKind == JsonValueKind.Number && ver.GetInt32() == 1));
                        bool isUniversal = scriptObj.TryGetProperty("isUniversal", out var uni) && (uni.ValueKind == JsonValueKind.True || (uni.ValueKind == JsonValueKind.Number && uni.GetInt32() == 1));
                        bool isPatched = scriptObj.TryGetProperty("isPatched", out var pat) && (pat.ValueKind == JsonValueKind.True || (pat.ValueKind == JsonValueKind.Number && pat.GetInt32() == 1));

                        bool hasKey = false;
                        if (scriptObj.TryGetProperty("key", out var k))
                        {
                            if (k.ValueKind == JsonValueKind.True || k.ValueKind == JsonValueKind.False)
                                hasKey = k.GetBoolean();
                            else if (k.ValueKind == JsonValueKind.String)
                                hasKey = !string.IsNullOrEmpty(k.GetString()) && k.GetString().ToLower() != "false" && k.GetString() != "0";
                        }

                        int views = scriptObj.TryGetProperty("views", out var vw) && vw.ValueKind == JsonValueKind.Number ? vw.GetInt32() : 0;
                        int likes = scriptObj.TryGetProperty("likeCount", out var lk) && lk.ValueKind == JsonValueKind.Number ? lk.GetInt32() : 0;
                        string scriptType = scriptObj.TryGetProperty("scriptType", out var st) ? st.GetString() : "free";

                        string dateString = "";
                        if (scriptObj.TryGetProperty("createdAt", out var ca))
                        {
                            if (ca.ValueKind == JsonValueKind.String && DateTime.TryParse(ca.GetString(), out DateTime parsedDate))
                            {
                                dateString = parsedDate.ToString("dd/MM/yyyy");
                            }
                        }

                        if (chkVerified != null && chkVerified.Checked && !isVerified) continue;
                        if (chkUniversal != null && chkUniversal.Checked && !isUniversal) continue;
                        if (chkPatched != null && chkPatched.Checked && !isPatched) continue;

                        if (cmbKeySystemFilter != null && cmbKeySystemFilter.SelectedIndex > 0)
                        {
                            string keySel = cmbKeySystemFilter.SelectedItem?.ToString();
                            if (keySel == "Yes" && !hasKey) continue;
                            if (keySel == "No" && hasKey) continue;
                        }

                        AddGridScriptCard(title, gameName, imageUrl, isVerified, isUniversal, views, likes, scriptType, dateString, scriptCode);
                    }

                    if (count == 0)
                    {
                        hasMorePages = false;
                    }
                }
                else
                {
                    hasMorePages = false;
                }
            }
        }

        private void AddGridScriptCard(string title, string gameName, string imageUrl, bool isVerified, bool isUniversal, int views, int likes, string scriptType, string dateString, string scriptCode)
        {
            if (flowScriptContainer == null) return;

            int netWidth = flowScriptContainer.Width - SystemInformation.VerticalScrollBarWidth - 24;
            int gapBetweenCards = 10;
            int dynamicCardWidth = Math.Max(160, (netWidth - gapBetweenCards - 12) / 2);

            int totalGridWidth = (dynamicCardWidth * 2) + gapBetweenCards;
            int horizontalPadding = Math.Max(4, (netWidth - totalGridWidth) / 2);

            flowScriptContainer.Padding = new Padding(horizontalPadding, 10, horizontalPadding, 10);

            Guna.UI2.WinForms.Guna2Panel card = new Guna.UI2.WinForms.Guna2Panel();
            card.Size = new DrawingSize(dynamicCardWidth, 335);
            card.FillColor = DrawingColor.FromArgb(12, 12, 12);
            card.BorderRadius = 12;
            card.BorderColor = DrawingColor.FromArgb(35, 35, 35);
            card.BorderThickness = 1;
            card.Margin = new Padding(gapBetweenCards / 2, 0, gapBetweenCards / 2, 12);

            int innerWidth = dynamicCardWidth - 24;

            PictureBox picBox = new PictureBox();
            picBox.Size = new DrawingSize(innerWidth, 130);
            picBox.Location = new DrawingPoint(12, 12);
            picBox.SizeMode = PictureBoxSizeMode.Zoom;
            picBox.BackColor = DrawingColor.FromArgb(18, 18, 18);

            if (!string.IsNullOrEmpty(imageUrl))
            {
                if (imageUrl.StartsWith("/")) imageUrl = "https://scriptblox.com" + imageUrl;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (HttpClient imgClient = new HttpClient())
                        {
                            byte[] bytes = await imgClient.GetByteArrayAsync(imageUrl);
                            using (var ms = new MemoryStream(bytes))
                            {
                                System.Drawing.Image img = System.Drawing.Image.FromStream(ms);
                                if (!picBox.IsDisposed)
                                {
                                    picBox.Invoke((MethodInvoker)(() => { picBox.Image = img; }));
                                }
                            }
                        }
                    }
                    catch { }
                });
            }
            card.Controls.Add(picBox);

            FormLabel lblTitle = new FormLabel();
            lblTitle.Text = title;
            lblTitle.Font = new DrawingFont("Segoe UI", 9.5F, DrawingFontStyle.Bold);
            lblTitle.ForeColor = DrawingColor.White;
            lblTitle.BackColor = DrawingColor.FromArgb(12, 12, 12);
            lblTitle.Location = new DrawingPoint(12, 150);
            lblTitle.Size = new DrawingSize(innerWidth, 42);
            lblTitle.AutoSize = false;
            card.Controls.Add(lblTitle);

            Guna.UI2.WinForms.Guna2Panel gamePill = new Guna.UI2.WinForms.Guna2Panel();
            gamePill.Size = new DrawingSize(innerWidth, 24);
            gamePill.Location = new DrawingPoint(12, 196);
            gamePill.FillColor = DrawingColor.FromArgb(22, 22, 22);
            gamePill.BorderRadius = 4;

            FormLabel lblGame = new FormLabel();
            lblGame.Text = gameName;
            lblGame.Font = new DrawingFont("Segoe UI", 7.5F, DrawingFontStyle.Regular);
            lblGame.ForeColor = DrawingColor.DarkGray;
            lblGame.BackColor = DrawingColor.Transparent;
            lblGame.Location = new DrawingPoint(6, 4);
            lblGame.AutoSize = true;
            gamePill.Controls.Add(lblGame);
            card.Controls.Add(gamePill);

            FormLabel lblStats = new FormLabel();
            lblStats.Text = $"👁 {views:N0}  ⭐ {likes}  📥 {scriptType}";
            lblStats.Font = new DrawingFont("Segoe UI", 7.5F);
            lblStats.ForeColor = DrawingColor.DarkGray;
            lblStats.BackColor = DrawingColor.FromArgb(12, 12, 12);
            lblStats.Location = new DrawingPoint(12, 228);
            lblStats.AutoSize = true;
            card.Controls.Add(lblStats);

            FormLabel lblDate = new FormLabel();
            lblDate.Text = dateString;
            lblDate.Font = new DrawingFont("Segoe UI", 8F);
            lblDate.ForeColor = DrawingColor.Gray;
            lblDate.BackColor = DrawingColor.FromArgb(12, 12, 12);
            lblDate.Location = new DrawingPoint(12, 252);
            lblDate.AutoSize = true;
            card.Controls.Add(lblDate);

            if (isVerified || isUniversal)
            {
                FormLabel lblBadge = new FormLabel();
                lblBadge.Text = isVerified ? "✔ VERIFIED" : "UNIVERSAL";
                lblBadge.Font = new DrawingFont("Segoe UI", 6.5F, DrawingFontStyle.Bold);
                lblBadge.ForeColor = isVerified ? DrawingColor.LightGreen : DrawingColor.LightGray;
                lblBadge.BackColor = DrawingColor.FromArgb(12, 12, 12);
                lblBadge.Location = new DrawingPoint(Math.Max(85, dynamicCardWidth - 85), 252);
                lblBadge.AutoSize = true;
                card.Controls.Add(lblBadge);
            }

            int btnCopyWidth = 60;
            int btnOpenWidth = 64;
            int btnExecuteWidth = Math.Max(50, innerWidth - (btnCopyWidth + btnOpenWidth + 12));

            Guna.UI2.WinForms.Guna2Button btnCopy = new Guna.UI2.WinForms.Guna2Button();
            btnCopy.Text = "Copy";
            btnCopy.Size = new DrawingSize(btnCopyWidth, 28);
            btnCopy.Location = new DrawingPoint(12, 285);
            btnCopy.FillColor = DrawingColor.Transparent;
            btnCopy.UseTransparentBackground = true;
            btnCopy.ForeColor = DrawingColor.White;
            btnCopy.BorderRadius = 6;
            btnCopy.BorderColor = DrawingColor.FromArgb(50, 50, 50);
            btnCopy.BorderThickness = 1;
            btnCopy.Font = new DrawingFont("Segoe UI", 8F, DrawingFontStyle.Bold);
            btnCopy.Click += (s, e) => { Clipboard.SetText(scriptCode); };
            card.Controls.Add(btnCopy);

            Guna.UI2.WinForms.Guna2Button btnOpenTab = new Guna.UI2.WinForms.Guna2Button();
            btnOpenTab.Text = "Open";
            btnOpenTab.Size = new DrawingSize(btnOpenWidth, 28);
            btnOpenTab.Location = new DrawingPoint(12 + btnCopyWidth + 6, 285);
            btnOpenTab.FillColor = DrawingColor.Transparent;
            btnOpenTab.UseTransparentBackground = true;
            btnOpenTab.ForeColor = DrawingColor.White;
            btnOpenTab.BorderRadius = 6;
            btnOpenTab.BorderColor = DrawingColor.FromArgb(50, 50, 50);
            btnOpenTab.BorderThickness = 1;
            btnOpenTab.Font = new DrawingFont("Segoe UI", 8F, DrawingFontStyle.Bold);
            btnOpenTab.Click += async (s, e) =>
            {
                await SetActiveTabScript(scriptCode);
                if (scriptHubPanel != null) scriptHubPanel.Visible = false;
                if (webView21 != null) webView21.Visible = true;
                if (bottomActionPanel != null) bottomActionPanel.Visible = true;
            };
            card.Controls.Add(btnOpenTab);

            Guna.UI2.WinForms.Guna2Button btnExecute = new Guna.UI2.WinForms.Guna2Button();
            btnExecute.Text = "Execute";
            btnExecute.Size = new DrawingSize(btnExecuteWidth, 28);
            btnExecute.Location = new DrawingPoint(12 + btnCopyWidth + btnOpenWidth + 12, 285);
            btnExecute.FillColor = DrawingColor.Transparent;
            btnExecute.UseTransparentBackground = true;
            btnExecute.ForeColor = DrawingColor.White;
            btnExecute.BorderRadius = 6;
            btnExecute.BorderColor = DrawingColor.FromArgb(50, 50, 50);
            btnExecute.BorderThickness = 1;
            btnExecute.Font = new DrawingFont("Segoe UI", 8F, DrawingFontStyle.Bold);
            btnExecute.Click += (s, clickArgs) =>
            {
                QuorumAPI.QuorumModule.ExecuteScript(scriptCode);
            };
            card.Controls.Add(btnExecute);

            flowScriptContainer.Controls.Add(card);
        }

        private async void guna2Button5_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Lua files (*.lua)|*.lua|All files (*.*)|*.*";
                openFileDialog.Title = "Open Lua Script";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string fileContent = File.ReadAllText(openFileDialog.FileName);

                        if (webView21 != null && webView21.CoreWebView2 != null)
                        {
                            string escapedContent = System.Text.Json.JsonSerializer.Serialize(fileContent);
                            await webView21.CoreWebView2.ExecuteScriptAsync($"editor.setValue({escapedContent});");
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to open file: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        public class AppSettings
        {
            public bool AlwaysOnTop { get; set; } = false;
            public bool UnlockFps { get; set; } = false;
        }

        public class EditorTabItem
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
        }

        public class EditorSessionState
        {
            public string ActiveTabId { get; set; }
            public List<EditorTabItem> Tabs { get; set; } = new List<EditorTabItem>();
        }

        private async void guna2Button4_Click(object sender, EventArgs e)
        {
            if (webView21 != null && webView21.CoreWebView2 != null)
            {
                try
                {
                    await webView21.CoreWebView2.ExecuteScriptAsync("editor.setValue('');");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to clear editor: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}