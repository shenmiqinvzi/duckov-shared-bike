# 下载 / 手动安装

创意工坊条目：<https://steamcommunity.com/sharedfiles/filedetails/?id=3834463519>

不想用创意工坊（或订阅后没生效）时，用这个压缩包手动安装，内容和工坊条目完全一致。

## 文件

| 文件 | 版本 | 大小 | SHA256 |
| --- | --- | --- | --- |
| `DuckovSharedBike-1.0.17.zip` | 1.0.17 | 975,894 字节（0.93 MB） | `D7755E5066AFF6F7ABC6B43B8D4969F0304DC6F2599F6DFD52E3BB217E7245C0` |

校验：

```powershell
Get-FileHash .\DuckovSharedBike-1.0.17.zip -Algorithm SHA256
```

## 安装

1. 解压，得到 `DuckovSharedBike` 文件夹（里面 11 个文件）。
2. 把它整个放进：

   ```
   <Steam 库>\steamapps\common\Escape from Duckov\Duckov_Data\Mods\DuckovSharedBike
   ```

   默认路径是 `C:\Program Files (x86)\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Mods\`。
   `Mods` 目录不存在就自己建一个。
3. 重启游戏，在模组菜单里启用「共享单车」。

目录结构必须正好是这个形状（多一层文件夹就读不到 DLL）：

```
Duckov_Data\Mods\DuckovSharedBike\DuckovSharedBike.dll
Duckov_Data\Mods\DuckovSharedBike\0Harmony.dll
Duckov_Data\Mods\DuckovSharedBike\info.ini
...
```

`DuckovSharedBike.pdb` 只是调试符号，删掉不影响运行。

## 升级 / 卸载

- 升级：关掉游戏，用新版本覆盖同一个文件夹（`info.ini` 里的 `publishedFileId` 保留即可）。
- 卸载：关掉游戏，删掉 `Duckov_Data\Mods\DuckovSharedBike` 整个文件夹。

## 版本说明

每个版本的改动见仓库里的 `docs\修复记录-<版本>.md`，1.0.17 是 [站点加密到十五处](../docs/修复记录-1.0.17.md)。

## 这是怎么来的

```powershell
scripts\Build.ps1        # 编译 + 打包到 artifacts\DuckovSharedBike-<版本>.zip
```

`dist\` 里的压缩包就是 `Build.ps1` 的产物，未做任何改动。
