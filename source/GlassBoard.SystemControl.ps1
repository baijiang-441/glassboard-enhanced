# GlassBoard control center only. Uses Windows PowerShell, WinRT and host native
# helpers. No media sessions, lyrics engine, downloaded runtime or network client.
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$pipeInput=New-Object IO.StreamReader([Console]::OpenStandardInput(),(New-Object Text.UTF8Encoding($false,$true)))
$pipeOutput=New-Object IO.StreamWriter([Console]::OpenStandardOutput(),(New-Object Text.UTF8Encoding($false)))
$pipeOutput.AutoFlush=$true
[Console]::SetIn($pipeInput)
[Console]::SetOut($pipeOutput)
$script:controlsReady=$false
function Initialize-Controls {
    if($script:controlsReady){return}
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $script:radioType=[Windows.Devices.Radios.Radio, Windows.Devices.Radios, ContentType=WindowsRuntime]
    $script:radioStateType=[Windows.Devices.Radios.RadioState, Windows.Devices.Radios, ContentType=WindowsRuntime]
    $script:radioAccessType=[Windows.Devices.Radios.RadioAccessStatus, Windows.Devices.Radios, ContentType=WindowsRuntime]
    $script:radioListType=([type]'System.Collections.Generic.IReadOnlyList`1').MakeGenericType($script:radioType)
    $script:deviceType=[Windows.Devices.Enumeration.DeviceInformation, Windows.Devices.Enumeration, ContentType=WindowsRuntime]
    $script:deviceListType=[Windows.Devices.Enumeration.DeviceInformationCollection, Windows.Devices.Enumeration, ContentType=WindowsRuntime]
    $script:deviceKindType=[Windows.Devices.Enumeration.DeviceInformationKind, Windows.Devices.Enumeration, ContentType=WindowsRuntime]
    $script:unpairType=[Windows.Devices.Enumeration.DeviceUnpairingResult, Windows.Devices.Enumeration, ContentType=WindowsRuntime]
    $script:asTask=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
    } | Select-Object -First 1
    if($null -eq $script:asTask){throw 'Windows async support is unavailable'}
    $script:controlsReady=$true
}
function Await-Control($operation,$resultType) {
    $task=$script:asTask.MakeGenericMethod($resultType).Invoke($null,@($operation))
    if(-not $task.Wait(4000)){throw 'Windows control operation timed out'}
    # Keep an empty/singleton WinRT collection from becoming null/a scalar.
    return ,$task.Result
}
function Read-Radios {
    Initialize-Controls
    $radios=Await-Control ($script:radioType::GetRadiosAsync()) $script:radioListType
    return @($radios | Where-Object {$_.Kind.ToString() -eq 'WiFi' -or $_.Kind.ToString() -eq 'Bluetooth'})
}
function Read-State {
    $state=@{Audio=$null;Brightness=$null;Radios=@();Errors=@()}
    try{$state.Audio=[GlassBoard.NativeAudio]::State()}catch{$state.Errors+=('音频：'+$_.Exception.GetBaseException().Message)}
    try{$state.Brightness=[GlassBoard.NativeBrightness]::Read()}catch{$state.Errors+=('亮度：'+$_.Exception.GetBaseException().Message)}
    try{$state.Radios=@(Read-Radios | ForEach-Object {@{Kind=$_.Kind.ToString();Name=$_.Name;Enabled=$_.State.ToString() -eq 'On';Available=$_.State.ToString() -ne 'Disabled'}})}catch{$state.Errors+=('无线：'+$_.Exception.GetBaseException().Message)}
    return @{System=$state}
}
function Set-Radio($kind,$enabled) {
    if($kind -ne 'WiFi' -and $kind -ne 'Bluetooth'){throw 'Invalid radio kind'}
    Initialize-Controls
    $access=Await-Control ($script:radioType::RequestAccessAsync()) $script:radioAccessType
    if($access.ToString() -ne 'Allowed'){throw 'Windows 未授权无线开关，请在系统设置中操作。'}
    $radios=@(Read-Radios | Where-Object {$_.Kind.ToString() -eq $kind})
    if($radios.Count -eq 0){throw '未找到对应无线设备'}
    $target=[Enum]::Parse($script:radioStateType,($(if($enabled){'On'}else{'Off'})))
    foreach($radio in $radios){
        $result=Await-Control ($radio.SetStateAsync($target)) $script:radioAccessType
        if($result.ToString() -ne 'Allowed'){throw ('无线状态修改被 Windows 拒绝：'+$result)}
    }
}
function Read-Bluetooth {
    Initialize-Controls
    $query='System.Devices.Aep.ProtocolId:="{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}" AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True'
    $kind=[Enum]::Parse($script:deviceKindType,'AssociationEndpoint')
    $devices=Await-Control ($script:deviceType::FindAllAsync($query,[string[]]@('System.Devices.Aep.IsConnected'),$kind)) $script:deviceListType
    return @{Devices=@($devices | ForEach-Object {@{Id=$_.Id;Name=$_.Name;Paired=$_.Pairing.IsPaired;Connected=$_.Properties['System.Devices.Aep.IsConnected'] -eq $true}})}
}
function Dispatch-Control($request) {
    switch($request.Action){
        'state' {return Read-State}
        'radio' {Set-Radio $request.Text $request.Enabled;return @{}}
        'networks' {return @{Networks=@([GlassBoard.NativeWireless]::Networks($request.Enabled))}}
        'connect' {[GlassBoard.NativeWireless]::Connect($request.Adapter,$request.Text,$request.Profile,$request.Password,$request.Auth,$request.Cipher);return @{}}
        'disconnect' {[GlassBoard.NativeWireless]::Disconnect($request.Adapter);return @{}}
        'bluetooth' {return Read-Bluetooth}
        'unpair' {
            Initialize-Controls
            $device=Await-Control ($script:deviceType::CreateFromIdAsync($request.Key)) $script:deviceType
            $result=Await-Control ($device.Pairing.UnpairAsync()) $script:unpairType
            if($result.Status.ToString() -ne 'Unpaired' -and $result.Status.ToString() -ne 'AlreadyUnpaired'){throw ('取消配对未完成：'+$result.Status)}
            return @{}
        }
        'volume' {[GlassBoard.NativeAudio]::Set([double]$request.Value,$null);return @{}}
        'mute' {[GlassBoard.NativeAudio]::Set($null,[bool]$request.Enabled);return @{}}
        'output' {[GlassBoard.NativeAudio]::Output($request.Key);return @{}}
        'brightness' {[GlassBoard.NativeBrightness]::Set([double]$request.Value);return @{}}
        default {throw 'Unknown control action'}
    }
}
while($null -ne ($line=[Console]::ReadLine())){
    $request=$null;$id=0
    try{
        if($line.Length -gt 131072){throw 'Request too large'}
        $request=ConvertFrom-Json -InputObject $line
        if($null -eq $request -or $request.Version -ne 1 -or [int]$request.Id -lt 1){throw 'Invalid protocol version or request id'}
        $id=[int]$request.Id
        $result=Dispatch-Control $request
        $reply=@{Version=1;Id=$id;Ok=$true;Result=$result}
    }catch{$reply=@{Version=1;Id=$id;Ok=$false;Error=$_.Exception.GetBaseException().Message}}
    finally{if($null -ne $request -and $request.PSObject.Properties['Password']){$request.Password=''}}
    [Console]::WriteLine((ConvertTo-Json -InputObject $reply -Depth 12 -Compress))
}
