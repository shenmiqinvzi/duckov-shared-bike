$ErrorActionPreference = 'Stop'
$dir = 'E:\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Managed'
foreach ($n in @('UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.UI.dll','UnityEngine.UIModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.AIModule.dll','Unity.InputSystem.dll','ItemStatsSystem.dll','Newtonsoft.Json.dll','EasySave3.dll','Eflatun.SceneReference.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.TextCoreModule.dll','UnityEngine.TextCoreFontEngineModule.dll','UnityEngine.TextCoreTextEngineModule.dll')) {
    $p = Join-Path $dir $n
    if (Test-Path $p) { try { [void][System.Reflection.Assembly]::LoadFrom($p) } catch { } }
}
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir 'TeamSoda.Duckov.Core.dll'))
$item = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir 'ItemStatsSystem.dll'))
$flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$missing = New-Object System.Collections.Generic.List[string]

function Need([Type]$type, [string]$name, [string]$kind, [Type[]]$params = $null) {
    if ($null -eq $type) { $missing.Add("type null for $name"); return }
    $found = $false
    if ($kind -eq 'method') {
        if ($params) { $found = $null -ne $type.GetMethod($name, $flags, $null, $params, $null) }
        else { $found = ($type.GetMethods($flags) | Where-Object Name -eq $name).Count -gt 0 }
    } elseif ($kind -eq 'property') { $found = $null -ne $type.GetProperty($name, $flags) }
    elseif ($kind -eq 'field') { $found = $null -ne $type.GetField($name, $flags) }
    if (-not $found) { $missing.Add("$($type.Name).$name") }
}

$cmc = $asm.GetType('CharacterMainControl')
Need $cmc 'CanMove' method
Need $cmc 'CharacterWalkSpeed' property
Need $cmc 'CharacterMoveability' property
Need $cmc 'CurrentAction' property
Need $cmc 'AnimationMoveSpeedValue' property
Need $cmc 'SetPosition' method ([Type[]]@([UnityEngine.Vector3]))
Need $cmc 'Trigger' method ([Type[]]@([bool],[bool],[bool]))
Need $cmc 'SetRunInput' method ([Type[]]@([bool]))
Need $cmc 'SetAdsInput' method ([Type[]]@([bool]))
Need $cmc 'StartAction' method ([Type[]]@($asm.GetType('CharacterActionBase')))
foreach ($n in 'Attack','StartSkillAim','ReleaseSkill','PickupItem','UseItem','Dash','DropAllItems','CanRun','CanUseHand') { Need $cmc $n method }

$mov = $asm.GetType('Movement')
Need $mov 'MovementEnabled' property
Need $mov 'MoveInput' property
Need $mov 'ForceTurnTo' method ([Type[]]@([UnityEngine.Vector3]))

$ib = $asm.GetType('InteractableBase')
foreach ($n in 'otherInterablesInGroup','_interactbleList','interactTime','finishWhenTimeOut','interactMarkerOffset') { Need $ib $n field }
Need $ib 'InteractName' property
Need $ib 'IsInteractable' method ([Type[]]@())

Need ($item.GetType('ItemStatsSystem.Stat')) 'Value' property
Need ($item.GetType('ItemStatsSystem.Item')) 'CanDrop' property
Need ($asm.GetType('ItemAgent_Gun')) 'TransToFire' method
Need ($asm.GetType('ItemAgent_Gun')) 'ShootOneBullet' method

Need ($asm.GetType('CountDownArea')) 'BeginCountDown' method
Need ($asm.GetType('CountDownArea')) 'OnCountdownSucceed' method
Need ($asm.GetType('Duckov.Scenes.MultiSceneTeleporter')) 'DoTeleport' method
Need ($asm.GetType('SceneLoaderProxy')) 'LoadScene' method
Need ($asm.GetType('SceneLoaderProxy')) 'LoadMainMenu' method
Need ($asm.GetType('SceneLoader')) 'LoadBaseScene' method
Write-Host ("SceneLoader.LoadScene overloads=" + (($asm.GetType('SceneLoader')).GetMethods($flags) | Where-Object Name -eq 'LoadScene').Count)

$save = $asm.GetType('Saves.SavesSystem')
foreach ($n in 'SaveFile','CollectSaveData','KeyExisits') { Need $save $n method }
Need $save 'CurrentSlot' property
Need $save 'CurrentFilePath' property
$lm = $asm.GetType('LevelManager')
Need $lm 'IsBaseLevel' property
Need $lm 'LevelInited' property
Need $lm 'NotifyEvacuated' method

$sd = $asm.GetType('Duckov.UI.StorageDock')
Need $sd 'Instance' property
Need $sd 'open' property
Need ($asm.GetType('Duckov.UI.View')) 'ActiveView' property
Need ($asm.GetType('Duckov.Economy.EconomyManager')) 'Cash' property
Need ($asm.GetType('Duckov.Economy.EconomyManager')) 'Pay' method
try {
    Need ($asm.GetType('Duckov.MiniMaps.SimplePointOfInterest')) 'Create' method ([Type[]]@([UnityEngine.Vector3],[string],[string],[UnityEngine.Sprite],[bool]))
} catch { Write-Host "POI type check skipped: $($_.Exception.Message)" }
Need ($asm.GetType('CharacterAnimationControl_MagicBlend')) 'Update' method
Need ($asm.GetType('CharacterModelAudioPoster')) 'PostFootStepSound' method
Need ($asm.GetType('CharacterModel')) 'LateUpdate' method

$es3 = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir 'EasySave3.dll'))
$io = $es3.GetType('ES3Internal.ES3IO')
Write-Host "ES3IO=$($null -ne $io)"
if ($io) { Need $io 'CommitBackup' method }

if ($missing.Count -eq 0) { Write-Host 'ALL CONTRACT MEMBERS FOUND' } else { Write-Host ('MISSING: ' + ($missing -join ', ')) }
