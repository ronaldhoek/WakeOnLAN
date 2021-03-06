'    WakeOnLAN - Wake On LAN
'    Copyright (C) 2004-2018 Aquila Technology, LLC. <webmaster@aquilatech.com>
'
'    This file is part of WakeOnLAN.
'
'    WakeOnLAN is free software: you can redistribute it and/or modify
'    it under the terms of the GNU General Public License as published by
'    the Free Software Foundation, either version 3 of the License, or
'    (at your option) any later version.
'
'    WakeOnLAN is distributed in the hope that it will be useful,
'    but WITHOUT ANY WARRANTY; without even the implied warranty of
'    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
'    GNU General Public License for more details.
'
'    You should have received a copy of the GNU General Public License
'    along with WakeOnLAN.  If not, see <http://www.gnu.org/licenses/>.

Imports System.Net
Imports System.Management
Imports System.Linq
Imports AlphaWindow
Imports Machines
Imports WakeOnLan.My

Public Class Search

    Private Structure Profile
        Public Name As String
        Public IpAddress As String
        Public OsName As String
        Public NetInterface As String
        Public MacAddress As String
        Public WakeEnabled As String
        Public PowerManagementEnable As String
        Public PowerManagementActive As String
        Public WakeOnMagicOnly As String
    End Structure

    Private ReadOnly _lvwColumnSorter As ListViewColumnSorter
    Private ReadOnly _none As String = "--" & Resources.Strings.lit_None & "--"

    Private Sub Search_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        If Settings.SearchWindowSize.Width <> 0 Then
            Location = Settings.SearchWindowLocation
            Size = Settings.SearchWindowSize
        End If

        If Not IsOnScreen(Me) Then
            Location = New Point(0, 0)
        End If

        GetListViewState(listView, Settings.SearchColumns)

        IpAddressControl_Start.Text = MySettings.Default.SearchStart
        IpAddressControl_End.Text = MySettings.Default.SearchEnd

        ComboBoxGroup.Items.Clear()
        ComboBoxGroup.Items.Add(_none)

        Dim groups() As String = (From machine As Machine In Machines
                                  Where machine.Group <> ""
                                  Select machine.Group).Distinct().ToArray()

        ComboBoxGroup.Items.AddRange(groups)
        ComboBoxGroup.Text = _none
    End Sub

    Private Sub Search_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        Settings.SearchColumns = SaveListViewState(listView)
        Settings.SearchWindowLocation = Location
        Settings.SearchWindowSize = Size
    End Sub

    Private Sub OKButton_Click(ByVal sender As Object, ByVal e As EventArgs) Handles OKButton.Click
        Dim machine As Machine

        For Each listViewItem As ListViewItem In listView.CheckedItems
            Machines.Remove(listViewItem.SubItems(0).Text)

            machine = New Machine
            machine.Name = listViewItem.SubItems(0).Text
            machine.MAC = listViewItem.SubItems(4).Text
            machine.IP = listViewItem.SubItems(3).Text
            machine.Broadcast = IPAddress.Broadcast.ToString()
            machine.Netbios = listViewItem.SubItems(0).Text
            machine.Emergency = True
            machine.UDPPort = 9
            machine.TTL = 128
            machine.RDPPort = 3389
            machine.ShutdownCommand = String.Empty
            If (ComboBoxGroup.Text <> _none) Then
                machine.Group = ComboBoxGroup.Text
            End If

            Machines.Add(machine)
        Next
        Machines.Save()

        MySettings.Default.SearchStart = IpAddressControl_Start.Text
        MySettings.Default.SearchEnd = IpAddressControl_End.Text

        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub closeButton_Click(sender As Object, e As EventArgs) Handles closeButton.Click
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    Private Sub Poll(ip As String, progress As Integer)
        Dim profile As Profile

        Try
            ToolStripStatusLabel1.Text = String.Format(Resources.Strings.Polling, ip)

            If Not Computer.Network.Ping(ip) Then
                backgroundWorker.ReportProgress(progress, Nothing)
                Exit Sub
            End If

            profile = GetWmIdata(ip)
            backgroundWorker.ReportProgress(progress, profile)

        Catch ex As Exception
            backgroundWorker.ReportProgress(progress, Nothing)

        End Try

    End Sub

    Private Function GetWmIdata(ip As String) As Profile
        Dim scopeCimv2, scopeWmi As ManagementScope
        Dim managementObject As ManagementObject
        Dim searcher As ManagementObjectSearcher
        Dim queryCollection As ManagementObjectCollection
        Dim wmiQuery As ObjectQuery
        Dim profile As New Profile

        Try
			Tracelog.WriteLine("Entering GetWMIdata <" & ip & ">")
			Tracelog.Indent()

			scopeCimv2 = New ManagementScope("\\" & ip & "\root\cimv2")
            scopeCimv2.Connect()
            scopeWmi = New ManagementScope("\\" & ip & "\root\WMI")
            scopeWmi.Connect()

            ' Query system for Operating System information
            wmiQuery = New ObjectQuery("SELECT * FROM Win32_OperatingSystem")
            searcher = New ManagementObjectSearcher(scopeCimv2, wmiQuery)
            queryCollection = searcher.Get()

            For Each managementObject In queryCollection
                profile.Name = managementObject("csname")
				Tracelog.WriteLine("profile.name " & profile.Name)
				profile.OsName = managementObject("Caption")
				Tracelog.WriteLine("profile.osname " & profile.OsName)
				Exit For
            Next

            wmiQuery = New ObjectQuery("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True")
            searcher = New ManagementObjectSearcher(scopeCimv2, wmiQuery)

            queryCollection = searcher.Get()

            For Each managementObject In queryCollection
                For Each s As String In managementObject("IPaddress")
                    profile.IpAddress = s
					Tracelog.WriteLine("profile.ipaddress " & profile.IpAddress)
					Exit For
                Next

                wmiQuery = New ObjectQuery("SELECT * FROM Win32_NetWorkAdapter WHERE AdapterTypeId=0 AND Index='" & managementObject("Index") & "'")
                searcher = New ManagementObjectSearcher(scopeCimv2, wmiQuery)

                For Each managementObjectAdapter As ManagementObject In searcher.Get()
                    profile.NetInterface = managementObjectAdapter("Description")
					Tracelog.WriteLine("profile.netinterface " & profile.NetInterface)
					profile.MacAddress = managementObjectAdapter("MACAddress")
					Tracelog.WriteLine("profile.macaddress " & profile.MacAddress)

					Try
                        wmiQuery = New ObjectQuery("SELECT * FROM MSPower_DeviceEnable")
                        searcher2 = New ManagementObjectSearcher(scopeWmi, wmiQuery)
                        For Each managementObject2 As ManagementObject In searcher2.Get()
                            If (managementObject2("InstanceName").ToString().ToUpper.StartsWith(managementObjectAdapter("PNPDeviceID"))) Then
                                profile.PowerManagementEnable = IIf(managementObject2("Enable"), Resources.Strings.lit_true, Resources.Strings.lit_false)
                                profile.PowerManagementActive = IIf(managementObject2("Active"), Resources.Strings.lit_true, Resources.Strings.lit_false)
								Tracelog.WriteLine("profile.PowerManagementActive " & profile.PowerManagementActive)
								Exit For
                            End If
                        Next

                        wmiQuery = New ObjectQuery("SELECT * FROM MSPower_DeviceWakeEnable")
                        searcher2 = New ManagementObjectSearcher(scopeWmi, wmiQuery)
                        For Each managementObject2 As ManagementObject In searcher2.Get()
                            If (managementObject2("InstanceName").ToString().ToUpper.StartsWith(managementObjectAdapter("PNPDeviceID"))) Then
                                profile.WakeEnabled = IIf(managementObject2("Enable"), Resources.Strings.lit_true, Resources.Strings.lit_false)
								Tracelog.WriteLine("profile.WakeEnabled " & profile.WakeEnabled)
								Exit For
                            End If
                        Next

                        wmiQuery = New ObjectQuery("SELECT * FROM MSNdis_DeviceWakeOnMagicPacketOnly")
                        searcher2 = New ManagementObjectSearcher(scopeWmi, wmiQuery)
                        For Each managementObject2 As ManagementObject In searcher2.Get()
                            If (managementObject2("InstanceName").ToString().ToUpper.StartsWith(managementObjectAdapter("PNPDeviceID"))) Then
                                profile.WakeOnMagicOnly = IIf(managementObject2("EnableWakeOnMagicPacketOnly"), Resources.Strings.lit_true, Resources.Strings.lit_false)
								Tracelog.WriteLine("profile.WakeOnMagicOnly " & profile.WakeOnMagicOnly)
								Exit For
                            End If
                        Next

                    Catch ex As Exception
						Tracelog.WriteLine("Exception1: " & ex.Message)

					End Try

                Next
            Next

        Catch ex As Exception
			Tracelog.WriteLine("Exception2: " & ex.Message)
			profile.IpAddress = ip
            profile = FindMAC(profile)

        End Try

        If String.IsNullOrEmpty(profile.Name) Then profile.Name = ip
        If String.IsNullOrEmpty(profile.PowerManagementEnable) Then profile.PowerManagementEnable = Resources.Strings.lit_Unknown
        If String.IsNullOrEmpty(profile.PowerManagementActive) Then profile.PowerManagementActive = Resources.Strings.lit_Unknown
        If String.IsNullOrEmpty(profile.WakeEnabled) Then profile.WakeEnabled = Resources.Strings.lit_Unknown
        If String.IsNullOrEmpty(profile.WakeOnMagicOnly) Then profile.WakeOnMagicOnly = Resources.Strings.lit_Unknown

		Tracelog.UnIndent()
		Tracelog.WriteLine("Leaving GetWMIdata")
		Return profile

    End Function

    Private Function FindMAC(profile As Profile) As Profile
        Dim address As IPAddress
        Dim remoteIp As Int32
        Dim mac() As Byte = New Byte(6) {}
        Dim len As Integer = 6
        Dim hostEntry As IPHostEntry

        Try
			Tracelog.WriteLine("Entering FindMAC")
			Tracelog.Indent()

			address = IPAddress.Parse(profile.IpAddress)
            remoteIp = address.GetHashCode()
			Tracelog.WriteLine("IP: " & address.ToString())

			If remoteIp <> 0 Then
                If SendARP(remoteIp, 0, mac, len) = 0 Then
                    profile.MacAddress = BitConverter.ToString(mac, 0, len)
					Tracelog.WriteLine("MAC: " & profile.MacAddress)
					hostEntry = Dns.GetHostEntry(address)
                    profile.Name = hostEntry.HostName
					Tracelog.WriteLine("name: " & hostEntry.HostName)
					profile.OsName = Resources.Strings.lit_Unknown
                End If
            End If

        Catch ex As Exception
            profile.OsName = ex.Message
			Tracelog.WriteLine("Exception: " & ex.Message)

		End Try

		Tracelog.UnIndent()
		Tracelog.WriteLine("Leaving FindMAC")

		Return profile

    End Function

    Private Sub searchButton_Click(sender As Object, e As EventArgs) Handles SearchBegin.Click
        Cursor = Cursors.WaitCursor
        SearchBegin.Enabled = False
        cancelSearch.Enabled = True
        ToolStripProgressBar1.Visible = True
        listView.ListViewItemSorter = Nothing
        listView.Items.Clear()
        backgroundWorker.RunWorkerAsync()
    End Sub

    Private Sub cancelSearch_Click(sender As Object, e As EventArgs) Handles cancelSearch.Click
        backgroundWorker.CancelAsync()
    End Sub

    Private Sub backgroundWorker_DoWork(sender As Object, e As System.ComponentModel.DoWorkEventArgs) Handles backgroundWorker.DoWork
        Dim i, startIp, stopIp As UInt32
        Dim ip As String
        Dim progress As Integer

        Try
            startIp = IpToInt(IPAddress.Parse(IpAddressControl_Start.Text))
            stopIp = IpToInt(IPAddress.Parse(IpAddressControl_End.Text))

            For i = startIp To stopIp
                ip = IPAddress.Parse(i).ToString()
                If (ip.EndsWith(".255")) Then
                    Continue For
                End If
                progress = (i - startIp) * 100 / Math.Max(1, stopIp - startIp)
                Poll(ip, progress)
                If backgroundWorker.CancellationPending Then
                    Exit For
                End If
                Application.DoEvents()
            Next

        Catch ex As Exception
            MessageBox.Show(ex.Message, "Error in Search worker", MessageBoxButtons.OK, MessageBoxIcon.Error)

        End Try

    End Sub

    Private Sub backgroundWorker_ProgressChanged(sender As Object, e As System.ComponentModel.ProgressChangedEventArgs) Handles backgroundWorker.ProgressChanged
        Dim i As ListViewItem
        Dim p As Profile

        Try
			Tracelog.WriteLine("Entering backgroundWorker_ProgressChanged")
			Tracelog.Indent()

			Tracelog.WriteLine("e.UserState null? " & (e.UserState Is Nothing))
			If Not (e.UserState Is Nothing) Then
                p = e.UserState
				Tracelog.WriteLine("adding p.Name")
				i = listView.Items.Add(p.Name)
				Tracelog.WriteLine("adding p.OsName")
				i.SubItems.Add(p.OsName)
				Tracelog.WriteLine("adding p.NetInterface")
				i.SubItems.Add(p.NetInterface)
				Tracelog.WriteLine("adding p.IpAddress")
				i.SubItems.Add(p.IpAddress)
				Tracelog.WriteLine("adding p.MacAddress")
				i.SubItems.Add(p.MacAddress)
				Tracelog.WriteLine("adding p.WakeEnabled")
				i.SubItems.Add(p.WakeEnabled)
            End If

			Tracelog.WriteLine("writing progress: " & e.ProgressPercentage)
			ToolStripProgressBar1.Value = e.ProgressPercentage

        Catch ex As Exception
			Tracelog.WriteLine("Search::backgroundWorker_ProgressChanged Exception: " & ex.Message)
			'MessageBox.Show(ex.Message, "Error in [backgroundWorker_ProgressChanged]", MessageBoxButtons.OK, MessageBoxIcon.Error)

		End Try

		Tracelog.UnIndent()
		Tracelog.WriteLine("Leaving backgroundWorker_ProgressChanged")

	End Sub

    Private Sub backgroundWorker_RunWorkerCompleted(sender As Object, e As System.ComponentModel.RunWorkerCompletedEventArgs) Handles backgroundWorker.RunWorkerCompleted
        SearchBegin.Enabled = True
        cancelSearch.Enabled = False
        Cursor = Cursors.Default
        ToolStripStatusLabel1.Text = Resources.Strings.Done
        ToolStripProgressBar1.Visible = False
        listView.ListViewItemSorter = _lvwColumnSorter
    End Sub

    Private Sub CheckAllButton_Click(sender As Object, e As EventArgs) Handles CheckAllButton.Click
        For Each l As ListViewItem In listView.Items
            l.Checked = True
        Next
    End Sub

    Private Sub UnCheckAllButton_Click(sender As Object, e As EventArgs) Handles UnCheckAllButton.Click
        For Each l As ListViewItem In listView.Items
            l.Checked = False
        Next
    End Sub

    Private Sub ShowDetails(l As ListViewItem)
        Dim profile As Profile
        Dim s As String

        If l.SubItems(0).Text = "" Then
            l.StateImageIndex = 2
            MsgBox(l.SubItems(1).Text, MsgBoxStyle.Exclamation + MsgBoxStyle.OkOnly, "Error")
            Exit Sub
        End If

        profile = GetWmIdata(l.SubItems(3).Text)
#If DISPLAY Then
        profile.MacAddress = profile.MacAddress.Substring(0, 9) & "00:00:00"
#End If

        s = "OS: " & profile.OsName & vbCrLf
        s &= "Network adapter: " & profile.NetInterface & vbCrLf
        s &= "IP: " & profile.IpAddress & vbCrLf
        s &= "MAC: " & profile.MacAddress & vbCrLf & vbCrLf
        s &= "WakeOnLAN Enabled: " & profile.WakeEnabled & vbCrLf
        s &= "Power Management Enabled: " & profile.PowerManagementEnable & vbCrLf
        s &= "Wake by Magic Packet only: " & profile.WakeOnMagicOnly

        MsgBox(s, MsgBoxStyle.OkOnly + MsgBoxStyle.Information, l.Text & " settings")

    End Sub

    Private Sub listView_DoubleClick(sender As Object, e As EventArgs) Handles listView.DoubleClick
        listView.Cursor = Cursors.WaitCursor
        ShowDetails(listView.SelectedItems(0))
        listView.Cursor = Cursors.Default
    End Sub

    Private Sub listView_ColumnClick(sender As Object, e As ColumnClickEventArgs) Handles listView.ColumnClick
        ' Determine if the clicked column is already the column that is 
        ' being sorted.
        If (e.Column = _lvwColumnSorter.SortColumn) Then
            ' Reverse the current sort direction for this column.
            If (_lvwColumnSorter.Order = SortOrder.Ascending) Then
                _lvwColumnSorter.Order = SortOrder.Descending
            Else
                _lvwColumnSorter.Order = SortOrder.Ascending
            End If
        Else
            ' Set the column number that is to be sorted; default to ascending.
            _lvwColumnSorter.SortColumn = e.Column
            _lvwColumnSorter.Order = SortOrder.Ascending
        End If
        If (e.Column = 3) Then
            _lvwColumnSorter.ObjectType = "IP"
        Else
            _lvwColumnSorter.ObjectType = "String"
        End If

        Settings.SearchSortColumn = _lvwColumnSorter.SortColumn
        Settings.SearchSortDirection = _lvwColumnSorter.Order

        ' Perform the sort with these new sort options.
        listView.SetSortIcon(_lvwColumnSorter.SortColumn, _lvwColumnSorter.Order)
        listView.Sort()
    End Sub

    Public Sub New()

        ' This call is required by the designer.
        InitializeComponent()

        ' Create an instance of a ListView column sorter and assign it 
        ' to the ListView control.
        _lvwColumnSorter = New ListViewColumnSorter()
        listView.ListViewItemSorter = _lvwColumnSorter
        _lvwColumnSorter.SortColumn = Settings.SearchSortColumn
        _lvwColumnSorter.Order = Settings.SearchSortDirection
        listView.SetSortIcon(_lvwColumnSorter.SortColumn, _lvwColumnSorter.Order)
    End Sub

    Private Sub Search_Resize(sender As Object, e As EventArgs) Handles Me.Resize
		Tracelog.WriteLine("resize: " & Me.Size.ToString())
	End Sub

    Private Sub listView_Resize(sender As Object, e As EventArgs) Handles listView.Resize
        If listView.Columns.Count = 0 Then Return

        Dim resizeColumn As Integer = 1
        Dim w As Integer = 0
        For column As Integer = 0 To listView.Columns.Count - 1
            If column <> resizeColumn Then w += listView.Columns(column).Width
        Next
        w = listView.ClientSize.Width - w - 1 - SystemInformation.VerticalScrollBarWidth
        If w > 0 Then listView.Columns(resizeColumn).Width = w
    End Sub

    Public Function SaveListViewState(ByVal listview As ListView) As String
        Return listview.Columns.Cast(Of ColumnHeader)().Aggregate("", Function(current, c) current & (c.Width & " "))
    End Function

    Public Sub GetListViewState(ByVal listview As ListView, ByVal state As String)
        Dim s() As String
        Dim i As Int16

        s = Split(state)
        If (UBound(s) <> listview.Columns.Count) Then Exit Sub

        For i = 0 To UBound(s) - 1
            listview.Columns(i).Width = Int(s(i))
        Next
    End Sub

End Class
