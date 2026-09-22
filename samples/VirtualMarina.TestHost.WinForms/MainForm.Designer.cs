namespace VirtualMarina.TestHost.WinForms
{
    partial class MainForm
    {
        /// <summary>Required designer variable.</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>Clean up any resources being used.</summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            splitContainer = new SplitContainer();
            marinaView = new VirtualMarina.WinForms.MarinaViewControl();
            grpEvents = new GroupBox();
            lstEvents = new ListBox();
            grpView = new GroupBox();
            lblStatistics = new Label();
            btnResetView = new Button();
            cmbCameraPreset = new ComboBox();
            lblCamera = new Label();
            cmbLabelMode = new ComboBox();
            lblLabels = new Label();
            chkTemporarilyFree = new CheckBox();
            chkReserved = new CheckBox();
            chkOccupied = new CheckBox();
            chkFree = new CheckBox();
            grpBerth = new GroupBox();
            btnReleaseBerth = new Button();
            btnMoorAlongside = new Button();
            btnResetFlags = new Button();
            btnReadOnly = new Button();
            btnMaintenance = new Button();
            btnSelectPier = new Button();
            btnShowActions = new Button();
            btnFocus = new Button();
            btnCheckOut = new Button();
            btnOwnerAway = new Button();
            btnReserve = new Button();
            btnCheckIn = new Button();
            txtBerthDetails = new TextBox();
            statusStrip = new StatusStrip();
            lblHover = new ToolStripStatusLabel();
            lblCameraPose = new ToolStripStatusLabel();
            lblHelp = new ToolStripStatusLabel();
            tmrStatus = new System.Windows.Forms.Timer(components);
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            grpEvents.SuspendLayout();
            grpView.SuspendLayout();
            grpBerth.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer
            // 
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.FixedPanel = FixedPanel.Panel2;
            splitContainer.Location = new Point(0, 0);
            splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(marinaView);
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(grpEvents);
            splitContainer.Panel2.Controls.Add(grpView);
            splitContainer.Panel2.Controls.Add(grpBerth);
            splitContainer.Panel2.Padding = new Padding(6);
            splitContainer.Size = new Size(1484, 839);
            splitContainer.SplitterDistance = 1024;
            splitContainer.TabIndex = 0;
            // 
            // marinaView
            // 
            marinaView.BackColor = Color.FromArgb(194, 217, 235);
            marinaView.Dock = DockStyle.Fill;
            marinaView.Location = new Point(0, 0);
            marinaView.Name = "marinaView";
            marinaView.Size = new Size(1024, 839);
            marinaView.TabIndex = 0;
            marinaView.RenderError += OnMarinaViewRenderError;
            marinaView.BerthSelected += OnBerthSelected;
            marinaView.MultiBerthSelected += OnMultiBerthSelected;
            marinaView.SelectionChanged += OnSelectionChanged;
            marinaView.BerthActionInvoked += OnBerthActionInvoked;
            marinaView.BerthHoverChanged += OnBerthHoverChanged;
            marinaView.BerthStatusChanged += OnBerthStatusChanged;
            marinaView.LayoutChanged += OnLayoutChanged;
            // 
            // grpEvents
            // 
            grpEvents.Controls.Add(lstEvents);
            grpEvents.Dock = DockStyle.Fill;
            grpEvents.Location = new Point(6, 506);
            grpEvents.Name = "grpEvents";
            grpEvents.Padding = new Padding(6);
            grpEvents.Size = new Size(444, 327);
            grpEvents.TabIndex = 2;
            grpEvents.TabStop = false;
            grpEvents.Text = "Events raised by the library";
            // 
            // lstEvents
            // 
            lstEvents.Dock = DockStyle.Fill;
            lstEvents.Font = new Font("Consolas", 8.5F);
            lstEvents.IntegralHeight = false;
            lstEvents.ItemHeight = 13;
            lstEvents.Location = new Point(6, 22);
            lstEvents.Name = "lstEvents";
            lstEvents.Size = new Size(432, 299);
            lstEvents.TabIndex = 0;
            // 
            // grpView
            // 
            grpView.Controls.Add(lblStatistics);
            grpView.Controls.Add(btnResetView);
            grpView.Controls.Add(cmbCameraPreset);
            grpView.Controls.Add(lblCamera);
            grpView.Controls.Add(cmbLabelMode);
            grpView.Controls.Add(lblLabels);
            grpView.Controls.Add(chkTemporarilyFree);
            grpView.Controls.Add(chkReserved);
            grpView.Controls.Add(chkOccupied);
            grpView.Controls.Add(chkFree);
            grpView.Dock = DockStyle.Top;
            grpView.Location = new Point(6, 346);
            grpView.Name = "grpView";
            grpView.Size = new Size(444, 160);
            grpView.TabIndex = 1;
            grpView.TabStop = false;
            grpView.Text = "View";
            // 
            // lblStatistics
            // 
            lblStatistics.AutoSize = true;
            lblStatistics.ForeColor = SystemColors.GrayText;
            lblStatistics.Location = new Point(12, 130);
            lblStatistics.Name = "lblStatistics";
            lblStatistics.Size = new Size(0, 15);
            lblStatistics.TabIndex = 9;
            // 
            // btnResetView
            // 
            btnResetView.Location = new Point(332, 89);
            btnResetView.Name = "btnResetView";
            btnResetView.Size = new Size(98, 25);
            btnResetView.TabIndex = 8;
            btnResetView.Text = "Reset view";
            btnResetView.UseVisualStyleBackColor = true;
            btnResetView.Click += OnResetViewClick;
            // 
            // cmbCameraPreset
            // 
            cmbCameraPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbCameraPreset.Location = new Point(90, 90);
            cmbCameraPreset.Name = "cmbCameraPreset";
            cmbCameraPreset.Size = new Size(236, 23);
            cmbCameraPreset.TabIndex = 7;
            cmbCameraPreset.SelectionChangeCommitted += OnCameraPresetSelected;
            // 
            // lblCamera
            // 
            lblCamera.AutoSize = true;
            lblCamera.Location = new Point(12, 93);
            lblCamera.Name = "lblCamera";
            lblCamera.Size = new Size(51, 15);
            lblCamera.TabIndex = 6;
            lblCamera.Text = "Camera:";
            // 
            // cmbLabelMode
            // 
            cmbLabelMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLabelMode.Items.AddRange(new object[] { "None", "Only free berths", "Non-occupied berths", "All berths" });
            cmbLabelMode.Location = new Point(90, 57);
            cmbLabelMode.Name = "cmbLabelMode";
            cmbLabelMode.Size = new Size(236, 23);
            cmbLabelMode.TabIndex = 5;
            cmbLabelMode.SelectedIndexChanged += OnLabelModeChanged;
            // 
            // lblLabels
            // 
            lblLabels.AutoSize = true;
            lblLabels.Location = new Point(12, 60);
            lblLabels.Name = "lblLabels";
            lblLabels.Size = new Size(67, 15);
            lblLabels.TabIndex = 4;
            lblLabels.Text = "Berth names:";
            // 
            // chkTemporarilyFree
            // 
            chkTemporarilyFree.AutoSize = true;
            chkTemporarilyFree.Checked = true;
            chkTemporarilyFree.CheckState = CheckState.Checked;
            chkTemporarilyFree.ForeColor = Color.DarkGoldenrod;
            chkTemporarilyFree.Location = new Point(270, 26);
            chkTemporarilyFree.Name = "chkTemporarilyFree";
            chkTemporarilyFree.Size = new Size(111, 19);
            chkTemporarilyFree.TabIndex = 3;
            chkTemporarilyFree.Text = "Temporarily free";
            chkTemporarilyFree.CheckedChanged += OnStatusFilterChanged;
            // 
            // chkReserved
            // 
            chkReserved.AutoSize = true;
            chkReserved.Checked = true;
            chkReserved.CheckState = CheckState.Checked;
            chkReserved.ForeColor = Color.RoyalBlue;
            chkReserved.Location = new Point(185, 26);
            chkReserved.Name = "chkReserved";
            chkReserved.Size = new Size(73, 19);
            chkReserved.TabIndex = 2;
            chkReserved.Text = "Reserved";
            chkReserved.CheckedChanged += OnStatusFilterChanged;
            // 
            // chkOccupied
            // 
            chkOccupied.AutoSize = true;
            chkOccupied.Checked = true;
            chkOccupied.CheckState = CheckState.Checked;
            chkOccupied.ForeColor = Color.Firebrick;
            chkOccupied.Location = new Point(96, 26);
            chkOccupied.Name = "chkOccupied";
            chkOccupied.Size = new Size(77, 19);
            chkOccupied.TabIndex = 1;
            chkOccupied.Text = "Occupied";
            chkOccupied.CheckedChanged += OnStatusFilterChanged;
            // 
            // chkFree
            // 
            chkFree.AutoSize = true;
            chkFree.Checked = true;
            chkFree.CheckState = CheckState.Checked;
            chkFree.ForeColor = Color.ForestGreen;
            chkFree.Location = new Point(12, 26);
            chkFree.Name = "chkFree";
            chkFree.Size = new Size(48, 19);
            chkFree.TabIndex = 0;
            chkFree.Text = "Free";
            chkFree.CheckedChanged += OnStatusFilterChanged;
            // 
            // grpBerth
            // 
            grpBerth.Controls.Add(btnReleaseBerth);
            grpBerth.Controls.Add(btnMoorAlongside);
            grpBerth.Controls.Add(btnResetFlags);
            grpBerth.Controls.Add(btnReadOnly);
            grpBerth.Controls.Add(btnMaintenance);
            grpBerth.Controls.Add(btnSelectPier);
            grpBerth.Controls.Add(btnShowActions);
            grpBerth.Controls.Add(btnFocus);
            grpBerth.Controls.Add(btnCheckOut);
            grpBerth.Controls.Add(btnOwnerAway);
            grpBerth.Controls.Add(btnReserve);
            grpBerth.Controls.Add(btnCheckIn);
            grpBerth.Controls.Add(txtBerthDetails);
            grpBerth.Dock = DockStyle.Top;
            grpBerth.Location = new Point(6, 6);
            grpBerth.Name = "grpBerth";
            grpBerth.Size = new Size(444, 340);
            grpBerth.TabIndex = 0;
            grpBerth.TabStop = false;
            grpBerth.Text = "Selected berth";
            // 
            // btnReleaseBerth
            // 
            btnReleaseBerth.Location = new Point(222, 304);
            btnReleaseBerth.Name = "btnReleaseBerth";
            btnReleaseBerth.Size = new Size(208, 27);
            btnReleaseBerth.TabIndex = 12;
            btnReleaseBerth.Text = "Release multi-berth";
            btnReleaseBerth.UseVisualStyleBackColor = true;
            btnReleaseBerth.Click += OnReleaseBerthClick;
            // 
            // btnMoorAlongside
            // 
            btnMoorAlongside.Location = new Point(12, 304);
            btnMoorAlongside.Name = "btnMoorAlongside";
            btnMoorAlongside.Size = new Size(204, 27);
            btnMoorAlongside.TabIndex = 11;
            btnMoorAlongside.Text = "Moor yacht alongside selection";
            btnMoorAlongside.UseVisualStyleBackColor = true;
            btnMoorAlongside.Click += OnMoorAlongsideClick;
            // 
            // btnResetFlags
            // 
            btnResetFlags.Location = new Point(292, 271);
            btnResetFlags.Name = "btnResetFlags";
            btnResetFlags.Size = new Size(138, 27);
            btnResetFlags.TabIndex = 10;
            btnResetFlags.Text = "Enable all berths";
            btnResetFlags.UseVisualStyleBackColor = true;
            btnResetFlags.Click += OnResetFlagsClick;
            // 
            // btnReadOnly
            // 
            btnReadOnly.Location = new Point(152, 271);
            btnReadOnly.Name = "btnReadOnly";
            btnReadOnly.Size = new Size(134, 27);
            btnReadOnly.TabIndex = 9;
            btnReadOnly.Text = "Lock (read-only)";
            btnReadOnly.UseVisualStyleBackColor = true;
            btnReadOnly.Click += OnReadOnlyClick;
            // 
            // btnMaintenance
            // 
            btnMaintenance.Location = new Point(12, 271);
            btnMaintenance.Name = "btnMaintenance";
            btnMaintenance.Size = new Size(134, 27);
            btnMaintenance.TabIndex = 8;
            btnMaintenance.Text = "Maintenance (disable)";
            btnMaintenance.UseVisualStyleBackColor = true;
            btnMaintenance.Click += OnMaintenanceClick;
            // 
            // btnSelectPier
            // 
            btnSelectPier.Location = new Point(292, 238);
            btnSelectPier.Name = "btnSelectPier";
            btnSelectPier.Size = new Size(138, 27);
            btnSelectPier.TabIndex = 7;
            btnSelectPier.Text = "Select pier / land";
            btnSelectPier.UseVisualStyleBackColor = true;
            btnSelectPier.Click += OnSelectPierClick;
            // 
            // btnShowActions
            // 
            btnShowActions.Location = new Point(152, 238);
            btnShowActions.Name = "btnShowActions";
            btnShowActions.Size = new Size(134, 27);
            btnShowActions.TabIndex = 6;
            btnShowActions.Text = "Show actions…";
            btnShowActions.UseVisualStyleBackColor = true;
            btnShowActions.Click += OnShowActionsClick;
            // 
            // btnFocus
            // 
            btnFocus.Location = new Point(12, 238);
            btnFocus.Name = "btnFocus";
            btnFocus.Size = new Size(134, 27);
            btnFocus.TabIndex = 5;
            btnFocus.Text = "Focus (top down)";
            btnFocus.UseVisualStyleBackColor = true;
            btnFocus.Click += OnFocusClick;
            // 
            // btnCheckOut
            // 
            btnCheckOut.Location = new Point(327, 205);
            btnCheckOut.Name = "btnCheckOut";
            btnCheckOut.Size = new Size(103, 27);
            btnCheckOut.TabIndex = 4;
            btnCheckOut.Text = "Check out";
            btnCheckOut.UseVisualStyleBackColor = true;
            btnCheckOut.Click += OnCheckOutClick;
            // 
            // btnOwnerAway
            // 
            btnOwnerAway.Location = new Point(222, 205);
            btnOwnerAway.Name = "btnOwnerAway";
            btnOwnerAway.Size = new Size(99, 27);
            btnOwnerAway.TabIndex = 3;
            btnOwnerAway.Text = "Owner away";
            btnOwnerAway.UseVisualStyleBackColor = true;
            btnOwnerAway.Click += OnOwnerAwayClick;
            // 
            // btnReserve
            // 
            btnReserve.Location = new Point(117, 205);
            btnReserve.Name = "btnReserve";
            btnReserve.Size = new Size(99, 27);
            btnReserve.TabIndex = 2;
            btnReserve.Text = "Reserve";
            btnReserve.UseVisualStyleBackColor = true;
            btnReserve.Click += OnReserveClick;
            // 
            // btnCheckIn
            // 
            btnCheckIn.Location = new Point(12, 205);
            btnCheckIn.Name = "btnCheckIn";
            btnCheckIn.Size = new Size(99, 27);
            btnCheckIn.TabIndex = 1;
            btnCheckIn.Text = "Check in";
            btnCheckIn.UseVisualStyleBackColor = true;
            btnCheckIn.Click += OnCheckInClick;
            // 
            // txtBerthDetails
            // 
            txtBerthDetails.BackColor = SystemColors.Window;
            txtBerthDetails.Font = new Font("Consolas", 9F);
            txtBerthDetails.Location = new Point(12, 24);
            txtBerthDetails.Multiline = true;
            txtBerthDetails.Name = "txtBerthDetails";
            txtBerthDetails.ReadOnly = true;
            txtBerthDetails.ScrollBars = ScrollBars.Vertical;
            txtBerthDetails.Size = new Size(418, 172);
            txtBerthDetails.TabIndex = 0;
            // 
            // statusStrip
            // 
            statusStrip.Items.AddRange(new ToolStripItem[] { lblHover, lblCameraPose, lblHelp });
            statusStrip.Location = new Point(0, 839);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new Size(1484, 22);
            statusStrip.TabIndex = 1;
            // 
            // lblHover
            // 
            lblHover.Name = "lblHover";
            lblHover.Size = new Size(0, 17);
            // 
            // lblCameraPose
            // 
            lblCameraPose.Name = "lblCameraPose";
            lblCameraPose.Size = new Size(876, 17);
            lblCameraPose.Spring = true;
            lblCameraPose.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblHelp
            // 
            lblHelp.Name = "lblHelp";
            lblHelp.Size = new Size(593, 17);
            lblHelp.Text = "Click: info | Ctrl/Shift+click: multi-select | Right-click: actions | Drag: pan | Right-drag: orbit | Wheel: zoom | Esc: close";
            // 
            // tmrStatus
            // 
            tmrStatus.Enabled = true;
            tmrStatus.Interval = 250;
            tmrStatus.Tick += OnStatusTimerTick;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1484, 861);
            Controls.Add(splitContainer);
            Controls.Add(statusStrip);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "VirtualMarina – Berth Desk (WinForms sample)";
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            grpEvents.ResumeLayout(false);
            grpView.ResumeLayout(false);
            grpView.PerformLayout();
            grpBerth.ResumeLayout(false);
            grpBerth.PerformLayout();
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private SplitContainer splitContainer;
        private VirtualMarina.WinForms.MarinaViewControl marinaView;
        private GroupBox grpBerth;
        private TextBox txtBerthDetails;
        private Button btnCheckIn;
        private Button btnReserve;
        private Button btnOwnerAway;
        private Button btnCheckOut;
        private Button btnFocus;
        private Button btnShowActions;
        private Button btnSelectPier;
        private Button btnMaintenance;
        private Button btnReadOnly;
        private Button btnResetFlags;
        private Button btnMoorAlongside;
        private Button btnReleaseBerth;
        private GroupBox grpView;
        private CheckBox chkFree;
        private CheckBox chkOccupied;
        private CheckBox chkReserved;
        private CheckBox chkTemporarilyFree;
        private Label lblLabels;
        private ComboBox cmbLabelMode;
        private Label lblCamera;
        private ComboBox cmbCameraPreset;
        private Button btnResetView;
        private Label lblStatistics;
        private GroupBox grpEvents;
        private ListBox lstEvents;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblHover;
        private ToolStripStatusLabel lblCameraPose;
        private ToolStripStatusLabel lblHelp;
        private System.Windows.Forms.Timer tmrStatus;
    }
}
