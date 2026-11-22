Imports Microsoft.Win32

Public Enum GpuVendor
    None
    Nvidia
    Amd
    Intel
    Other
End Enum

''' <summary>Detects the main GPU and its dedicated VRAM (used to pick TranslateGemma 4B vs 12B and CUDA/Vulkan/CPU builds).</summary>
Public Module HardwareInfo

    Public Class GpuDetails
        Public Property Name As String = "No dedicated GPU"
        Public Property Vendor As GpuVendor = GpuVendor.None
        Public Property VramBytes As Long = 0

        Public ReadOnly Property VramGB As Double
            Get
                Return VramBytes / 1024.0 / 1024.0 / 1024.0
            End Get
        End Property

        Public Overrides Function ToString() As String
            If Vendor = GpuVendor.None Then Return Loc.Tr(Name)
            Return $"{Name} ({VramGB:0.#} GB VRAM)"
        End Function
    End Class

    Private _gpu As GpuDetails

    ''' <summary>VRAM needed to keep each TranslateGemma size fully on the GPU (model + context + overhead).</summary>
    Public Const VramFor12B As Double = 10.0
    Public Const VramFor27B As Double = 22.0

    Public ReadOnly Property Gpu As GpuDetails
        Get
            If _gpu Is Nothing Then _gpu = Detect()
            Return _gpu
        End Get
    End Property

    ''' <summary>ollama.com/library/translategemma tag for this PC: 27b (22+ GB), 12b (10+ GB) or 4b.</summary>
    Public Function RecommendedTranslateSize() As String
        If Gpu.VramGB >= VramFor27B Then Return "27b"
        If Gpu.VramGB >= VramFor12B Then Return "12b"
        Return "4b"
    End Function

    ''' <summary>llama.cpp build: CUDA for NVIDIA, Vulkan for AMD / Intel, CPU without a usable GPU.</summary>
    Public Function RecommendedGpuBackend() As String
        Select Case Gpu.Vendor
            Case GpuVendor.Nvidia : Return "cuda"
            Case GpuVendor.None : Return "cpu"
            Case Else : Return "vulkan"
        End Select
    End Function

    Private Function Detect() As GpuDetails
        Dim best As New GpuDetails()
        Try
            ' Display adapters class key. HardwareInformation.qwMemorySize is a 64-bit value and
            ' (unlike WMI Win32_VideoController.AdapterRAM) is not capped at 4 GB.
            Const classKey = "SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
            Using root = Registry.LocalMachine.OpenSubKey(classKey)
                If root IsNot Nothing Then
                    For Each subName In root.GetSubKeyNames()
                        If Not System.Text.RegularExpressions.Regex.IsMatch(subName, "^\d{4}$") Then Continue For
                        Using k = root.OpenSubKey(subName)
                            If k Is Nothing Then Continue For
                            Dim desc = TryCast(k.GetValue("DriverDesc"), String)
                            If String.IsNullOrEmpty(desc) Then Continue For
                            Dim vram = ReadMemSize(k.GetValue("HardwareInformation.qwMemorySize"))
                            If vram <= 0 Then vram = ReadMemSize(k.GetValue("HardwareInformation.MemorySize"))
                            If vram > best.VramBytes Then
                                best = New GpuDetails With {.Name = desc, .VramBytes = vram, .Vendor = VendorFromName(desc)}
                            End If
                        End Using
                    Next
                End If
            End Using
        Catch ex As Exception
            Logger.Log("GPU registry detection failed: " & ex.Message)
        End Try

        ' nvidia-smi is the most accurate source on NVIDIA systems.
        Try
            Dim psi As New ProcessStartInfo("nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader,nounits") With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True}
            Using p = Process.Start(psi)
                Dim output = p.StandardOutput.ReadToEnd()
                p.WaitForExit(5000)
                For Each line In output.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    Dim parts = line.Split(","c)
                    If parts.Length >= 2 Then
                        Dim mib As Long
                        If Long.TryParse(parts(1).Trim(), mib) AndAlso mib * 1024L * 1024L > best.VramBytes Then
                            best = New GpuDetails With {.Name = parts(0).Trim(), .VramBytes = mib * 1024L * 1024L, .Vendor = GpuVendor.Nvidia}
                        End If
                    End If
                Next
            End Using
        Catch
            ' nvidia-smi not present – fine.
        End Try

        ' Integrated GPUs report tiny "dedicated" memory; treat < 1 GB as no usable GPU.
        If best.VramBytes < 1024L * 1024L * 1024L Then
            best = New GpuDetails With {.Name = If(best.VramBytes > 0, best.Name & " (integrated – CPU will be used)", "No dedicated GPU"), .VramBytes = 0}
        End If
        Logger.Log("Detected GPU: " & best.ToString())
        Return best
    End Function

    Private Function ReadMemSize(value As Object) As Long
        If value Is Nothing Then Return 0
        Try
            If TypeOf value Is Long Then Return CLng(value)
            If TypeOf value Is Integer Then Return CLng(CUInt(CInt(value) And &HFFFFFFFFL))
            Dim bytes = TryCast(value, Byte())
            If bytes IsNot Nothing Then
                If bytes.Length >= 8 Then Return BitConverter.ToInt64(bytes, 0)
                If bytes.Length >= 4 Then Return BitConverter.ToUInt32(bytes, 0)
            End If
        Catch
        End Try
        Return 0
    End Function

    Private Function VendorFromName(name As String) As GpuVendor
        Dim n = name.ToLowerInvariant()
        If n.Contains("nvidia") OrElse n.Contains("geforce") OrElse n.Contains("quadro") OrElse n.Contains("rtx") Then Return GpuVendor.Nvidia
        If n.Contains("amd") OrElse n.Contains("radeon") Then Return GpuVendor.Amd
        If n.Contains("intel") OrElse n.Contains("arc") Then Return GpuVendor.Intel
        Return GpuVendor.Other
    End Function

End Module
