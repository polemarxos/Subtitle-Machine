''' <summary>Translates the texts that the form designer put on controls (buttons, labels, columns, menus).</summary>
Public Module Localizer

    Public Sub Apply(root As Control)
        If Loc.Language = "en" Then Return
        TranslateControl(root)
    End Sub

    Private Function Tx(text As String) As String
        Return If(Loc.HasTranslation(text), Loc.Tr(text), text)
    End Function

    Private Sub TranslateControl(c As Control)
        If TypeOf c IsNot ComboBox AndAlso TypeOf c IsNot TextBoxBase Then c.Text = Tx(c.Text)
        Dim lv = TryCast(c, ListView)
        If lv IsNot Nothing Then
            For Each col As ColumnHeader In lv.Columns
                col.Text = Tx(col.Text)
            Next
        End If
        Dim grid = TryCast(c, DataGridView)
        If grid IsNot Nothing Then
            For Each col As DataGridViewColumn In grid.Columns
                col.HeaderText = Tx(col.HeaderText)
            Next
        End If
        If c.ContextMenuStrip IsNot Nothing Then TranslateItems(c.ContextMenuStrip.Items)
        For Each child As Control In c.Controls
            TranslateControl(child)
        Next
    End Sub

    Private Sub TranslateItems(items As ToolStripItemCollection)
        For Each it As ToolStripItem In items
            it.Text = Tx(it.Text)
            Dim mi = TryCast(it, ToolStripMenuItem)
            If mi IsNot Nothing Then TranslateItems(mi.DropDownItems)
        Next
    End Sub

End Module
