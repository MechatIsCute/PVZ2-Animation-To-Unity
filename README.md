# PVZ2-Animation-To-Unity
- 将PVZ2的动画资源导入Unity (Import PVZ2 Animation Resource Into Unity)
- 把 **Plants vs. Zombies 2** 的 XFL 动画源文件夹（素吧官网可以找到）转换为 **Unity Prefab + AnimationClip** 的编辑器插件。
## 特性

由于素吧的动画文件属于解包后的素材，丢失了很多命名信息，这个插件也只是通过这些解包的文件尽可能推测原有结构。如果按照源文件一比一写入，Zomboss的许多动画能产生上千上万的图层，所以插件对重复内容等尽可能地做了优化。

同时PVZ2的动画是将粒子效果直接写进动画的而不是单独的粒子播放器，所以你可能会在转换结果看到一大堆用相同纹理的物体。插件内置一套评估系统用来推测结果是不是粒子动画，你可以在插件中勾选粒子效果来将那些重复的资源转换为unity中的粒子发射器。但是这套评估系统并不是完美的，存在误判的情况，所以这里除非要自己实现粒子效果或者粒子资源太大，否则不推荐开启

由于是自己开发着玩的所以注释基本写的只有自己能看懂，也尝试过让AI来帮我补注释，但是可能是我用的AI比较垃圾给出的注释有很多废话，所以这里把所有注释都删除了，如果需要修改的话需要使用者自行理解（）
## 安装

将整个文件夹复制到项目的 `Assets/` 目录下即可。
## 使用

1. 菜单 **Tools → PVZ XFL → Unity** 打开窗口。

2. **源 XFL 文件夹**：选择含 `DOMDocument.xml` / `extra.json` / `LIBRARY/` `/main.xfl/`的文件夹。

3. **输出文件夹**：Assets 相对路径，如 `Assets/XflCharacters`。

4. 按需勾选选项，点 **开始转换**。

5. 生成结果：`<角色>.prefab`、`Animations/`、`Textures/`、`Particles/`。

### 运行时播放（可选）

本插件提供了一个简单的脚本来控制动画播放，可以用来测试功能是否可用

把 `XflPlayer` 挂到 Prefab 根节点（自动查找子物体的 `Animation` 组件）：

```csharp

var player = go.AddComponent<XflPlayer>();

player.Play("idle");       // 播放一次

player.PlayLoop("walk");   // 循环播放

player.IsPlaying("idle");

player.Stop();

```

段名即动画段名（`idle` / `walk` / `eat` / `die` / …）。

## 兼容

- 仅在Unity 6.5（URP）上进行了测试，低版本unity或者团结引擎不保证能正常运行

- 默认使用 Legacy `Animation`；勾选「状态机资源」则改用 `Animator`

## 说明

- 插件仅做格式转换，**不包含任何游戏素材**；PVZ 素材版权归原作者所有。
- 使用时请标注插件作者信息（就当为pvz同人圈的一点贡献，当然本人是不在意的）

## 关于AI在本项目中的使用
- 在UI排版美化、UI信息说明上使用了AI辅助工具

## 关于多语言
- 本插件为个人制作，加之PVZ同人游戏以中文语言居多，因此只支持中文。(This project only supports Chinese)
