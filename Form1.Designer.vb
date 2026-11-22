<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Form1
    Inherits System.Windows.Forms.Form

    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        components = New System.ComponentModel.Container()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(Form1))
        pnlHeader = New Panel()
        lblTitle = New Label()
        lblVersion = New Label()
        btnComponents = New Button()
        btnUpdate = New Button()
        pnlUpdate = New Panel()
        lblUpdate = New Label()
        btnRestart = New Button()
        pnlToolbar = New Panel()
        btnAddFiles = New Button()
        btnAddFolder = New Button()
        btnRemove = New Button()
        btnClear = New Button()
        btnCompare = New Button()
        mnuCompare = New ToolStripMenuItem()
        pnlList = New Panel()
        lblDropHint = New Label()
        lvTasks = New ListView()
        colFile = New ColumnHeader()
        colType = New ColumnHeader()
        colStatus = New ColumnHeader()
        colOutput = New ColumnHeader()
        ctxTasks = New ContextMenuStrip(components)
        mnuOpenFolder = New ToolStripMenuItem()
        mnuRemove = New ToolStripMenuItem()
        pnlOptions = New Panel()
        tblOptions = New TableLayoutPanel()
        lblSource = New Label()
        cmbSource = New ComboBox()
        lblTarget = New Label()
        btnTargets = New Button()
        lblVideoOut = New Label()
        cmbVideoOut = New ComboBox()
        lblWhenDone = New Label()
        cmbWhenDone = New ComboBox()
        mnuEdit = New ToolStripMenuItem()
        lblSubSource = New Label()
        cmbSubSource = New ComboBox()
        lblModel = New Label()
        cmbModel = New ComboBox()
        chkReplaceOriginal = New CheckBox()
        lblGpu = New Label()
        chkOverwrite = New CheckBox()
        pnlFooter = New Panel()
        progressBar = New ProgressBar()
        lblStatus = New Label()
        btnStart = New Button()
        btnStop = New Button()
        toolTip = New ToolTip(components)
        pnlHeader.SuspendLayout()
        pnlUpdate.SuspendLayout()
        pnlToolbar.SuspendLayout()
        pnlList.SuspendLayout()
        ctxTasks.SuspendLayout()
        pnlOptions.SuspendLayout()
        tblOptions.SuspendLayout()
        pnlFooter.SuspendLayout()
        SuspendLayout()
        '
        ' pnlHeader
        '
        pnlHeader.BackColor = Color.FromArgb(CByte(37), CByte(37), CByte(38))
        pnlHeader.Controls.Add(lblTitle)
        pnlHeader.Controls.Add(lblVersion)
        pnlHeader.Controls.Add(btnComponents)
        pnlHeader.Controls.Add(btnUpdate)
        pnlHeader.Dock = DockStyle.Top
        pnlHeader.Name = "pnlHeader"
        pnlHeader.Size = New Size(900, 56)
        '
        ' lblTitle
        '
        lblTitle.AutoSize = True
        lblTitle.Font = New Font("Segoe UI Semibold", 14.0F, FontStyle.Bold)
        lblTitle.ForeColor = Color.White
        lblTitle.Location = New Point(12, 12)
        lblTitle.Name = "lblTitle"
        lblTitle.Text = "Subtitle Machine"
        '
        ' lblVersion
        '
        lblVersion.AutoSize = True
        lblVersion.ForeColor = Color.FromArgb(CByte(150), CByte(150), CByte(150))
        lblVersion.Location = New Point(186, 21)
        lblVersion.Name = "lblVersion"
        lblVersion.Text = "v1.4.2"
        '
        ' btnComponents
        '
        btnComponents.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        btnComponents.BackColor = Color.FromArgb(CByte(51), CByte(51), CByte(55))
        btnComponents.FlatAppearance.BorderColor = Color.FromArgb(CByte(70), CByte(70), CByte(74))
        btnComponents.FlatStyle = FlatStyle.Flat
        btnComponents.ForeColor = Color.White
        btnComponents.Location = New Point(642, 12)
        btnComponents.Name = "btnComponents"
        btnComponents.Size = New Size(120, 32)
        btnComponents.Text = "Components"
        btnComponents.UseVisualStyleBackColor = False
        '
        ' btnUpdate
        '
        btnUpdate.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        btnUpdate.BackColor = Color.FromArgb(CByte(51), CByte(51), CByte(55))
        btnUpdate.FlatAppearance.BorderColor = Color.FromArgb(CByte(70), CByte(70), CByte(74))
        btnUpdate.FlatStyle = FlatStyle.Flat
        btnUpdate.ForeColor = Color.White
        btnUpdate.Location = New Point(768, 12)
        btnUpdate.Name = "btnUpdate"
        btnUpdate.Size = New Size(120, 32)
        btnUpdate.Text = "Check for updates"
        btnUpdate.UseVisualStyleBackColor = False
        '
        ' pnlUpdate
        '
        pnlUpdate.BackColor = Color.FromArgb(CByte(0), CByte(99), CByte(177))
        pnlUpdate.Controls.Add(lblUpdate)
        pnlUpdate.Controls.Add(btnRestart)
        pnlUpdate.Dock = DockStyle.Top
        pnlUpdate.Name = "pnlUpdate"
        pnlUpdate.Size = New Size(900, 36)
        pnlUpdate.Visible = False
        '
        ' lblUpdate
        '
        lblUpdate.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        lblUpdate.ForeColor = Color.White
        lblUpdate.Location = New Point(12, 9)
        lblUpdate.Name = "lblUpdate"
        lblUpdate.Size = New Size(740, 20)
        lblUpdate.Text = "A new version has been downloaded."
        '
        ' btnRestart
        '
        btnRestart.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        btnRestart.BackColor = Color.White
        btnRestart.FlatStyle = FlatStyle.Flat
        btnRestart.FlatAppearance.BorderSize = 0
        btnRestart.ForeColor = Color.FromArgb(CByte(0), CByte(99), CByte(177))
        btnRestart.Location = New Point(768, 5)
        btnRestart.Name = "btnRestart"
        btnRestart.Size = New Size(120, 26)
        btnRestart.Text = "Restart now"
        btnRestart.UseVisualStyleBackColor = False
        '
        ' pnlToolbar
        '
        pnlToolbar.Controls.Add(btnAddFiles)
        pnlToolbar.Controls.Add(btnAddFolder)
        pnlToolbar.Controls.Add(btnRemove)
        pnlToolbar.Controls.Add(btnClear)
        pnlToolbar.Controls.Add(btnCompare)
        pnlToolbar.Dock = DockStyle.Top
        pnlToolbar.Name = "pnlToolbar"
        pnlToolbar.Size = New Size(900, 50)
        '
        ' btnAddFiles
        '
        btnAddFiles.BackColor = Color.FromArgb(CByte(0), CByte(122), CByte(204))
        btnAddFiles.FlatAppearance.BorderSize = 0
        btnAddFiles.FlatStyle = FlatStyle.Flat
        btnAddFiles.ForeColor = Color.White
        btnAddFiles.Location = New Point(12, 10)
        btnAddFiles.Name = "btnAddFiles"
        btnAddFiles.Size = New Size(120, 32)
        btnAddFiles.Text = "+  Add files"
        btnAddFiles.UseVisualStyleBackColor = False
        '
        ' btnAddFolder
        '
        btnAddFolder.BackColor = Color.FromArgb(CByte(51), CByte(51), CByte(55))
        btnAddFolder.FlatAppearance.BorderColor = Color.FromArgb(CByte(70), CByte(70), CByte(74))
        btnAddFolder.FlatStyle = FlatStyle.Flat
        btnAddFolder.ForeColor = Color.White
        btnAddFolder.Location = New Point(138, 10)
        btnAddFolder.Name = "btnAddFolder"
        btnAddFolder.Size = New Size(120, 32)
        btnAddFolder.Text = "+  Add folder"
        btnAddFolder.UseVisualStyleBackColor = False
        '
        ' btnRemove
        '
        btnRemove.BackColor = Color.FromArgb(CByte(51), CByte(51), CByte(55))
        btnRemove.FlatAppearance.BorderColor = Color.FromArgb(CByte(70), CByte(70), CByte(74))
        btnRemove.FlatStyle = FlatStyle.Flat
        btnRemove.ForeColor = Color.White
        btnRemove.Location = New Point(264, 10)
        btnRemove.Name = "btnRemove"
        btnRemove.Size = New Size(100, 32)
        btnRemove.Text = "Remove"
        btnRemove.UseVisualStyleBackColor = False
        '
        ' btnClear
        '
        btnClear.BackColor = Color.FromArgb(CByte(51), CByte(51), CByte(55))
        btnClear.FlatAppearance.BorderColor = Color.FromArgb(CByte(70), CByte(70), CByte(74))
        btnClear.FlatStyle = FlatStyle.Flat
        btnClear.ForeColor = Color.White
        btnClear.Location = New Point(370, 10)
        btnClear.Name = "btnClear"
        btnClear.Size = New Size(100, 32)
        btnClear.Text = "Clear list"
        btnClear.UseVisualStyleBackColor = False
        ' 
        ' btnCompare
        ' 
        btnCompare.FlatStyle = FlatStyle.Flat
        btnCompare.Location = New Point(486, 10)
        btnCompare.Name = "btnCompare"
        btnCompare.Size = New Size(150, 32)
        btnCompare.Text = "Compare subtitles…"
        btnCompare.UseVisualStyleBackColor = False
        '
        ' pnlList
        '
        pnlList.Controls.Add(lblDropHint)
        pnlList.Controls.Add(lvTasks)
        pnlList.Dock = DockStyle.Fill
        pnlList.Name = "pnlList"
        pnlList.Padding = New Padding(12, 0, 12, 0)
        '
        ' lblDropHint
        '
        lblDropHint.Anchor = AnchorStyles.None
        lblDropHint.BackColor = Color.FromArgb(CByte(30), CByte(30), CByte(30))
        lblDropHint.Font = New Font("Segoe UI", 11.0F)
        lblDropHint.ForeColor = Color.FromArgb(CByte(130), CByte(130), CByte(130))
        lblDropHint.Location = New Point(200, 80)
        lblDropHint.Name = "lblDropHint"
        lblDropHint.Size = New Size(500, 60)
        lblDropHint.Text = "Drag && drop videos, audio or .srt files (or folders) here" & vbCrLf & "or use the Add files / Add folder buttons"
        lblDropHint.TextAlign = ContentAlignment.MiddleCenter
        '
        ' lvTasks
        '
        lvTasks.AllowDrop = True
        lvTasks.BackColor = Color.FromArgb(CByte(30), CByte(30), CByte(30))
        lvTasks.BorderStyle = BorderStyle.FixedSingle
        lvTasks.Columns.AddRange(New ColumnHeader() {colFile, colType, colStatus, colOutput})
        lvTasks.ContextMenuStrip = ctxTasks
        lvTasks.Dock = DockStyle.Fill
        lvTasks.ForeColor = Color.Gainsboro
        lvTasks.FullRowSelect = True
        lvTasks.Name = "lvTasks"
        lvTasks.OwnerDraw = True
        lvTasks.UseCompatibleStateImageBehavior = False
        lvTasks.View = View.Details
        '
        ' colFile / colType / colStatus / colOutput
        '
        colFile.Text = "File"
        colFile.Width = 330
        colType.Text = "Type"
        colType.Width = 70
        colStatus.Text = "Status"
        colStatus.Width = 270
        colOutput.Text = "Output"
        colOutput.Width = 180
        '
        ' ctxTasks
        '
        ctxTasks.Items.AddRange(New ToolStripItem() {mnuEdit, mnuCompare, mnuOpenFolder, mnuRemove})
        mnuCompare.Name = "mnuCompare"
        mnuCompare.Text = "Compare translation with original…"
        mnuEdit.Name = "mnuEdit"
        mnuEdit.Text = "Edit subtitles…"
        ctxTasks.Name = "ctxTasks"
        mnuOpenFolder.Name = "mnuOpenFolder"
        mnuOpenFolder.Text = "Open containing folder"
        mnuRemove.Name = "mnuRemove"
        mnuRemove.Text = "Remove from list"
        '
        ' pnlOptions
        '
        pnlOptions.Controls.Add(tblOptions)
        pnlOptions.Dock = DockStyle.Bottom
        pnlOptions.Name = "pnlOptions"
        pnlOptions.Padding = New Padding(12, 10, 12, 4)
        pnlOptions.Size = New Size(900, 176)
        '
        ' tblOptions
        '
        tblOptions.ColumnCount = 4
        tblOptions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120.0F))
        tblOptions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        tblOptions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120.0F))
        tblOptions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        tblOptions.Controls.Add(lblSource, 0, 0)
        tblOptions.Controls.Add(cmbSource, 1, 0)
        tblOptions.Controls.Add(lblTarget, 2, 0)
        tblOptions.Controls.Add(btnTargets, 3, 0)
        tblOptions.Controls.Add(lblModel, 0, 1)
        tblOptions.Controls.Add(cmbModel, 1, 1)
        tblOptions.Controls.Add(lblSubSource, 2, 1)
        tblOptions.Controls.Add(cmbSubSource, 3, 1)
        tblOptions.Controls.Add(chkReplaceOriginal, 0, 2)
        tblOptions.Controls.Add(chkOverwrite, 2, 2)
        tblOptions.Controls.Add(lblVideoOut, 0, 3)
        tblOptions.Controls.Add(cmbVideoOut, 1, 3)
        tblOptions.Controls.Add(lblWhenDone, 2, 3)
        tblOptions.Controls.Add(cmbWhenDone, 3, 3)
        tblOptions.Controls.Add(lblGpu, 0, 4)
        tblOptions.Dock = DockStyle.Fill
        tblOptions.Name = "tblOptions"
        tblOptions.RowCount = 5
        tblOptions.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
        tblOptions.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
        tblOptions.RowStyles.Add(New RowStyle(SizeType.Absolute, 30.0F))
        tblOptions.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
        tblOptions.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0F))
        tblOptions.SetColumnSpan(chkReplaceOriginal, 2)
        tblOptions.SetColumnSpan(lblGpu, 4)
        tblOptions.SetColumnSpan(chkOverwrite, 2)
        '
        ' lblSource
        '
        lblSource.Anchor = AnchorStyles.Left
        lblSource.AutoSize = True
        lblSource.ForeColor = Color.Gainsboro
        lblSource.Name = "lblSource"
        lblSource.Text = "Source language"
        '
        ' cmbSource
        '
        cmbSource.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        cmbSource.BackColor = Color.FromArgb(CByte(45), CByte(45), CByte(48))
        cmbSource.DropDownStyle = ComboBoxStyle.DropDownList
        cmbSource.FlatStyle = FlatStyle.Flat
        cmbSource.ForeColor = Color.White
        cmbSource.Name = "cmbSource"
        '
        ' lblTarget
        '
        lblTarget.Anchor = AnchorStyles.Left
        lblTarget.AutoSize = True
        lblTarget.ForeColor = Color.Gainsboro
        lblTarget.Margin = New Padding(16, 0, 3, 0)
        lblTarget.Name = "lblTarget"
        lblTarget.Text = "Translate to"
        '
        ' btnTargets (multi-language picker)
        ' 
        btnTargets.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        btnTargets.BackColor = Color.FromArgb(CByte(45), CByte(45), CByte(48))
        btnTargets.FlatAppearance.BorderColor = Color.FromArgb(CByte(70), CByte(70), CByte(74))
        btnTargets.FlatStyle = FlatStyle.Flat
        btnTargets.ForeColor = Color.White
        btnTargets.Name = "btnTargets"
        btnTargets.Size = New Size(200, 25)
        btnTargets.Text = "Greek"
        btnTargets.TextAlign = ContentAlignment.MiddleLeft
        btnTargets.UseVisualStyleBackColor = False
        ' 
        ' lblVideoOut / cmbVideoOut / lblWhenDone / cmbWhenDone
        ' 
        lblVideoOut.Anchor = AnchorStyles.Left
        lblVideoOut.AutoSize = True
        lblVideoOut.Name = "lblVideoOut"
        lblVideoOut.Text = "Video output"
        cmbVideoOut.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        cmbVideoOut.DropDownStyle = ComboBoxStyle.DropDownList
        cmbVideoOut.FlatStyle = FlatStyle.Flat
        cmbVideoOut.Name = "cmbVideoOut"
        lblWhenDone.Anchor = AnchorStyles.Left
        lblWhenDone.AutoSize = True
        lblWhenDone.Margin = New Padding(16, 0, 3, 0)
        lblWhenDone.Name = "lblWhenDone"
        lblWhenDone.Text = "When finished"
        cmbWhenDone.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        cmbWhenDone.DropDownStyle = ComboBoxStyle.DropDownList
        cmbWhenDone.FlatStyle = FlatStyle.Flat
        cmbWhenDone.Name = "cmbWhenDone"
        ' 
        ' lblSubSource
        ' 
        lblSubSource.Anchor = AnchorStyles.Left
        lblSubSource.AutoSize = True
        lblSubSource.ForeColor = Color.Gainsboro
        lblSubSource.Margin = New Padding(16, 0, 3, 0)
        lblSubSource.Name = "lblSubSource"
        lblSubSource.Text = "Create from"
        ' 
        ' cmbSubSource
        ' 
        cmbSubSource.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        cmbSubSource.BackColor = Color.FromArgb(CByte(45), CByte(45), CByte(48))
        cmbSubSource.DropDownStyle = ComboBoxStyle.DropDownList
        cmbSubSource.FlatStyle = FlatStyle.Flat
        cmbSubSource.ForeColor = Color.White
        cmbSubSource.Name = "cmbSubSource"
        '
        ' lblModel
        '
        lblModel.Anchor = AnchorStyles.Left
        lblModel.AutoSize = True
        lblModel.ForeColor = Color.Gainsboro
        lblModel.Name = "lblModel"
        lblModel.Text = "AI model"
        '
        ' cmbModel
        '
        cmbModel.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        cmbModel.BackColor = Color.FromArgb(CByte(45), CByte(45), CByte(48))
        cmbModel.DropDownStyle = ComboBoxStyle.DropDownList
        cmbModel.DropDownWidth = 420
        cmbModel.FlatStyle = FlatStyle.Flat
        cmbModel.ForeColor = Color.White
        cmbModel.Name = "cmbModel"
        '
        ' chkReplaceOriginal
        '
        chkReplaceOriginal.Anchor = AnchorStyles.Left
        chkReplaceOriginal.AutoSize = True
        chkReplaceOriginal.ForeColor = Color.Gainsboro
        chkReplaceOriginal.Margin = New Padding(3, 3, 3, 3)
        chkReplaceOriginal.Name = "chkReplaceOriginal"
        chkReplaceOriginal.Text = "Replace original subtitle file"
        '
        ' lblGpu
        '
        lblGpu.Anchor = AnchorStyles.Left
        lblGpu.AutoSize = True
        lblGpu.ForeColor = Color.FromArgb(CByte(130), CByte(130), CByte(130))
        lblGpu.Name = "lblGpu"
        lblGpu.Text = "GPU:"
        '
        ' chkOverwrite
        '
        chkOverwrite.Anchor = AnchorStyles.Left
        chkOverwrite.AutoSize = True
        chkOverwrite.ForeColor = Color.Gainsboro
        chkOverwrite.Margin = New Padding(16, 3, 3, 3)
        chkOverwrite.Name = "chkOverwrite"
        chkOverwrite.Text = "Redo files that are already finished"
        '
        ' pnlFooter
        '
        pnlFooter.BackColor = Color.FromArgb(CByte(37), CByte(37), CByte(38))
        pnlFooter.Controls.Add(progressBar)
        pnlFooter.Controls.Add(lblStatus)
        pnlFooter.Controls.Add(btnStart)
        pnlFooter.Controls.Add(btnStop)
        pnlFooter.Dock = DockStyle.Bottom
        pnlFooter.Name = "pnlFooter"
        pnlFooter.Size = New Size(900, 68)
        '
        ' progressBar
        '
        progressBar.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        progressBar.Location = New Point(12, 14)
        progressBar.Name = "progressBar"
        progressBar.Size = New Size(620, 12)
        progressBar.Style = ProgressBarStyle.Continuous
        '
        ' lblStatus
        '
        lblStatus.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        lblStatus.AutoEllipsis = True
        lblStatus.ForeColor = Color.Gainsboro
        lblStatus.Location = New Point(12, 34)
        lblStatus.Name = "lblStatus"
        lblStatus.Size = New Size(620, 22)
        lblStatus.Text = "Ready"
        '
        ' btnStart
        '
        btnStart.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        btnStart.BackColor = Color.FromArgb(CByte(0), CByte(122), CByte(204))
        btnStart.Enabled = False
        btnStart.FlatAppearance.BorderSize = 0
        btnStart.FlatStyle = FlatStyle.Flat
        btnStart.Font = New Font("Segoe UI Semibold", 10.0F, FontStyle.Bold)
        btnStart.ForeColor = Color.White
        btnStart.Location = New Point(648, 14)
        btnStart.Name = "btnStart"
        btnStart.Size = New Size(130, 40)
        btnStart.Text = "▶  Start"
        btnStart.UseVisualStyleBackColor = False
        '
        ' btnStop
        '
        btnStop.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        btnStop.BackColor = Color.FromArgb(CByte(170), CByte(60), CByte(55))
        btnStop.Enabled = False
        btnStop.FlatAppearance.BorderSize = 0
        btnStop.FlatStyle = FlatStyle.Flat
        btnStop.ForeColor = Color.White
        btnStop.Location = New Point(784, 14)
        btnStop.Name = "btnStop"
        btnStop.Size = New Size(104, 40)
        btnStop.Text = "■  Stop"
        btnStop.UseVisualStyleBackColor = False
        '
        ' Form1
        '
        AllowDrop = True
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        BackColor = Color.FromArgb(CByte(30), CByte(30), CByte(30))
        ClientSize = New Size(940, 680)
        ' Dock order: Fill first, then bottom panels, then top panels (last added = outermost).
        Controls.Add(pnlList)
        Controls.Add(pnlOptions)
        Controls.Add(pnlFooter)
        Controls.Add(pnlToolbar)
        Controls.Add(pnlUpdate)
        Controls.Add(pnlHeader)
        Font = New Font("Segoe UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point, CByte(0))
        ForeColor = Color.White
        Icon = CType(resources.GetObject("$this.Icon"), Icon)
        MinimumSize = New Size(840, 640)
        Name = "Form1"
        StartPosition = FormStartPosition.CenterScreen
        Text = "Subtitle Machine"
        pnlHeader.ResumeLayout(False)
        pnlHeader.PerformLayout()
        pnlUpdate.ResumeLayout(False)
        pnlToolbar.ResumeLayout(False)
        pnlList.ResumeLayout(False)
        ctxTasks.ResumeLayout(False)
        pnlOptions.ResumeLayout(False)
        tblOptions.ResumeLayout(False)
        tblOptions.PerformLayout()
        pnlFooter.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents pnlHeader As Panel
    Friend WithEvents lblTitle As Label
    Friend WithEvents lblVersion As Label
    Friend WithEvents btnComponents As Button
    Friend WithEvents btnUpdate As Button
    Friend WithEvents pnlUpdate As Panel
    Friend WithEvents lblUpdate As Label
    Friend WithEvents btnRestart As Button
    Friend WithEvents pnlToolbar As Panel
    Friend WithEvents btnAddFiles As Button
    Friend WithEvents btnAddFolder As Button
    Friend WithEvents btnRemove As Button
    Friend WithEvents btnClear As Button
    Friend WithEvents btnCompare As Button
    Friend WithEvents mnuCompare As ToolStripMenuItem
    Friend WithEvents pnlList As Panel
    Friend WithEvents lblDropHint As Label
    Friend WithEvents lvTasks As ListView
    Friend WithEvents colFile As ColumnHeader
    Friend WithEvents colType As ColumnHeader
    Friend WithEvents colStatus As ColumnHeader
    Friend WithEvents colOutput As ColumnHeader
    Friend WithEvents ctxTasks As ContextMenuStrip
    Friend WithEvents mnuOpenFolder As ToolStripMenuItem
    Friend WithEvents mnuRemove As ToolStripMenuItem
    Friend WithEvents pnlOptions As Panel
    Friend WithEvents tblOptions As TableLayoutPanel
    Friend WithEvents lblSource As Label
    Friend WithEvents cmbSource As ComboBox
    Friend WithEvents lblTarget As Label
    Friend WithEvents btnTargets As Button
    Friend WithEvents lblVideoOut As Label
    Friend WithEvents cmbVideoOut As ComboBox
    Friend WithEvents lblWhenDone As Label
    Friend WithEvents cmbWhenDone As ComboBox
    Friend WithEvents mnuEdit As ToolStripMenuItem
    Friend WithEvents lblSubSource As Label
    Friend WithEvents cmbSubSource As ComboBox
    Friend WithEvents lblModel As Label
    Friend WithEvents cmbModel As ComboBox
    Friend WithEvents chkReplaceOriginal As CheckBox
    Friend WithEvents lblGpu As Label
    Friend WithEvents chkOverwrite As CheckBox
    Friend WithEvents pnlFooter As Panel
    Friend WithEvents progressBar As ProgressBar
    Friend WithEvents lblStatus As Label
    Friend WithEvents btnStart As Button
    Friend WithEvents btnStop As Button
    Friend WithEvents toolTip As ToolTip
End Class
