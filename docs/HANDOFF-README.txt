给帮忙上传的人看的说明（鸭科夫共享单车 1.0.17）
================================================

你好，这是《逃离鸭科夫》的一个模组，麻烦你用自己的 Steam 账号把它传到创意工坊。
整个流程不超过五分钟，不需要编译、不需要改代码。

前提
----
1. 你的 Steam 账号**拥有**《逃离鸭科夫》（Escape from Duckov）。借家庭共享的账号不能公开发布。
2. 用你平时玩游戏的这台电脑，Steam 保持在线。
3. 游戏能正常启动、模组菜单能打开。


第一步：把文件夹放进游戏目录
----------------------------
把压缩包里的 `DuckovSharedBike` 整个文件夹放到：

    <你的 Steam 库>\steamapps\common\Escape from Duckov\Duckov_Data\Mods\DuckovSharedBike

例如默认路径是：

    C:\Program Files (x86)\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Mods\DuckovSharedBike

最终这个文件夹里应当正好是这 10 个文件（不要多也不要少）：

    DuckovSharedBike.dll      info.ini        preview.png     marker.png
    prototype.json            station-layout.json             README.txt
    0Harmony.dll              LICENSE-Harmony.txt             THIRD-PARTY.md

`Duckov_Data\Mods` 这个目录如果不存在，自己新建一个。


第二步：游戏内上传
------------------
1. 启动游戏，进入主菜单，打开「模组 / Mods」。
2. 列表里应当出现「共享单车」，状态是可启用。
3. 找到它这一行的**上传按钮**（图标是一个向上的箭头），点开。
4. 弹出的面板里：
   - 标题、描述、封面都是从 info.ini 与 preview.png 自动读的，**不用改**；
   - 如果显示「新建项目 / New」，说明这是新条目，正常；
   - 红字「你可能没有更新此物体的权限」只在**更新别人的条目**时才出现，新条目忽略；
   - `0 bytes / 0 bytes` 是正常现象，游戏不报字节进度；
   - 点「上传 / Upload」，然后等 3–5 分钟。
5. 成功后游戏会打开一个 Steam 网页，那就是你的工坊条目页。


第三步：把链接发回来
--------------------
把条目页地址（形如 https://steamcommunity.com/sharedfiles/filedetails/?id=XXXXXXXXXX）发给我。
另外把 `Duckov_Data\Mods\DuckovSharedBike\info.ini` 里新出现的那行
`publishedFileId=...` 也一起发回来，这样我这边下次更新就能对上同一条目。


出问题怎么办
------------
- **点了上传、等了一会没反应、日志里有 `Failed to upload mod. Result:k_EResultFail`**：
  这是新建条目后 Steam 需要几秒钟准备，直接再点一次上传即可，通常第二次就成功。
- **面板空白**：Steam 没登录或离线。退出游戏，确认 Steam 在线后重进。
- **列表里没有「共享单车」**：文件夹放错层了。必须是
  `...\Mods\DuckovSharedBike\DuckovSharedBike.dll`，不能是
  `...\Mods\DuckovSharedBike\DuckovSharedBike\DuckovSharedBike.dll`。
- **不改任何文件**：info.ini 里的版本号、描述、publishedFileId 都是刻意留下的；
  这个包里已经删掉了原来账号的 publishedFileId，所以你上传时会新建条目，这是预期的。


上传之后
--------
这条工坊条目从此属于你的账号：

- 条目可见性请设成 **公开**（拥有游戏的账号可以直接选公开）；
- 以后我每次更新，会把同样的压缩包发给你，你按第一、二步重放一次就是「更新条目」，链接不变；
- Steam 不支持把工坊条目的所有权转给别的账号，所以这条链接会一直挂在你的账号下——
  如果不方便长期代传，也可以随时告诉我，我改成在 GitHub 发布压缩包让玩家手动安装。

谢谢你。
