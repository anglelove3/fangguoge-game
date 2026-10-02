# 从零创建 Godot C# 项目 —— 手把手教学

> 目标：不依赖 AI 预创建的文件，自己从零搭建一个能运行的 Godot C# 项目
> 预计用时：30-40 分钟
> 适用版本：Godot 4.7.1 mono + .NET SDK 8.0

---

## 一、先理解：一个 Godot C# 项目最少需要哪些文件？

在动手之前，先知道"我们要造什么"。一个能运行的 Godot C# 项目，最少需要这 4 个东西：

| 文件 | 作用 | 比喻 |
|------|------|------|
| `project.godot` | 项目身份证（窗口大小、主场景、渲染方式） | 房子的设计图 |
| `项目名.csproj` | C# 编译配置（用哪个 .NET 版本） | 翻译官的工作手册 |
| 至少一个 `.tscn` 场景文件 | 游戏画面（节点树 + 布局） | 一个房间 |
| 至少一个 `.cs` 脚本文件 | 游戏逻辑（按钮点了做什么） | 房间里的电器说明书 |

**Godot 编辑器可以帮你自动生成前两个文件**，但后两个需要你自己在编辑器里创建。

---

## 二、第一步：用 Godot 创建一个新项目（10 分钟）

### Step 1：打开 Godot 项目管理器

双击运行：
`D:\Godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe`

### Step 2：点"新建项目"

在项目管理器界面，你会看到右上角有 **"新建项目"** 按钮，点它。

### Step 3：填写项目信息

弹出的窗口里有几个要填的：

| 填写项 | 填什么 | 说明 |
|--------|--------|------|
| **项目名称** | `从零创建测试` | 随便起，只是管理器里显示的名字 |
| **创建路径** | `D:\游戏创作\从零创建测试\` | 选一个空文件夹，不要选到我们现有的项目目录 |
| **渲染器** | 选 **兼容模式（Compatibility）** | 和我们的正式项目保持一致 |

> ⚠️ **注意**：Godot 4.7 的新建项目默认用 GDScript。我们要的是 C# 项目，但 Godot **没有直接的"C# 项目"选项**。所以先按默认创建，后面再手动加 C# 支持。

### Step 4：点"创建 & 编辑"

Godot 会：
1. 在 `D:\游戏创作\从零创建测试\` 创建文件夹
2. 自动生成 `project.godot`（项目配置）
3. 打开编辑器

✅ 看到编辑器界面 = 完成

---

## 三、第二步：看看 Godot 自动生成了什么（5 分钟）

打开文件资源管理器，到 `D:\游戏创作\从零创建测试\`，你会看到：

```
从零创建测试/
├── project.godot          ← Godot 自动生成的项目配置
└── .godot/                ← Godot 内部缓存文件夹（别动它）
```

**双击打开 `project.godot`**（用记事本就行），你会看到类似这样的内容：

```ini
; Engine configuration file.
config_version=5

[application]
config/name="从零创建测试"
config/features=PackedStringArray("4.7", "GL Compatibility")
run/main_scene=""

[display]
window/size/viewport_width=1152
window/size/viewport_height=648
window/stretch/mode="canvas_items"

[rendering]
renderer/rendering_method="gl_compatibility"
renderer/rendering_method.mobile="gl_compatibility"
```

**逐行解释：**

| 内容 | 意思 |
|------|------|
| `config_version=5` | 配置文件格式版本（Godot 4 用 5） |
| `config/name="从零创建测试"` | 项目名字 |
| `config/features=PackedStringArray("4.7", "GL Compatibility")` | 用的 Godot 版本和渲染方式 |
| `run/main_scene=""` | **主场景路径（现在是空的！）** 我们后面要填 |
| `window/size/viewport_width=1152` | 窗口宽度（默认 1152，我们要改成 1920） |
| `renderer/rendering_method="gl_compatibility"` | 用兼容模式渲染（对老电脑友好） |

> 💡 **关键理解**：`project.godot` 就是一个纯文本配置文件。Godot 编辑器改设置 = 改这个文件里的文字。你完全可以直接用记事本改，效果一样。

---

## 四、第三步：手动添加 C# 支持（10 分钟）

Godot 默认创建的是 GDScript 项目，要变成 C# 项目，需要做两件事：

### 4.1 创建 .csproj 文件

在项目文件夹 `D:\游戏创作\从零创建测试\` 下，**新建一个文本文件**，命名为：

`从零创建测试.csproj`

> ⚠️ 注意：文件名必须和 `project.godot` 里的 `project/assembly_name` 一致（如果有的话），或者和文件夹名一致。我们这里用文件夹名。

用记事本打开这个文件，写入以下内容：

```xml
<Project Sdk="Godot.NET.Sdk/4.7.1">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>从零创建测试</RootNamespace>
  </PropertyGroup>
</Project>
```

**逐行解释：**

| 内容 | 意思 |
|------|------|
| `<Project Sdk="Godot.NET.Sdk/4.7.1">` | 告诉 .NET：用 Godot 4.7.1 的 SDK 来编译 |
| `<TargetFramework>net8.0</TargetFramework>` | 用 .NET 8.0 编译（所以我们要装 8.0） |
| `<EnableDynamicLoading>true</EnableDynamicLoading>` | 允许 Godot 动态加载 C# 代码（热重载用） |
| `<RootNamespace>从零创建测试</RootNamespace>` | C# 命名空间（代码里的"家族姓氏"） |

> 💡 **关键理解**：`.csproj` 文件就是告诉电脑"用什么版本的 .NET 来翻译 C# 代码"。Godot 4.7.1 只认识 .NET 6/7/8，不认识 10，所以必须写 `net8.0`。

### 4.2 在 project.godot 里声明程序集名称

打开 `project.godot`，在 `[dotnet]` 段（如果没有就自己加）写入：

```ini
[dotnet]
project/assembly_name="从零创建测试"
```

这个值必须和 `.csproj` 文件名一致（去掉 `.csproj` 后缀）。

**完整的 `project.godot` 现在应该是：**

```ini
config_version=5

[application]
config/name="从零创建测试"
config/features=PackedStringArray("4.7", "GL Compatibility")
run/main_scene=""

[display]
window/size/viewport_width=1152
window/size/viewport_height=648
window/stretch/mode="canvas_items"

[dotnet]
project/assembly_name="从零创建测试"

[rendering]
renderer/rendering_method="gl_compatibility"
renderer/rendering_method.mobile="gl_compatibility"
```

### 4.3 回到 Godot 编辑器，构建一次

1. 切换到 Godot 编辑器窗口
2. 按 `Ctrl + Shift + B`（或菜单：项目 → 构建项目）
3. 底部输出面板应该显示：`Build succeeded with 0 warnings`

✅ 构建成功 = C# 环境配好了

> 如果报错"找不到 .NET SDK"：检查环境变量。命令行运行 `D:\Godot\.Net8.0\dotnet.exe --list-sdks` 应该显示 8.0.424。

---

## 五、第四步：在编辑器里创建第一个场景（10 分钟）

现在 C# 环境好了，但项目还是空的——没有场景，没有画面。我们来创建第一个场景。

### Step 1：创建场景文件

在 Godot 编辑器里：

1. 看左上角 **"场景"面板**，点 **"其他场景"** → **"新建场景"**
2. 弹出窗口问你要创建什么类型的根节点 → 选 **"用户界面"** → **"Control"**
3. 点"创建"

你会看到：
- 左上场景树出现了一个 `Control` 节点
- 中间视口区是灰色的（Control 节点默认透明）

### Step 2：保存场景

按 `Ctrl + S`（或菜单：场景 → 保存场景）

保存路径选：`res://scenes/main.tscn`

> `res://` 是 Godot 的"项目根目录"缩写。`res://scenes/main.tscn` = `D:\游戏创作\从零创建测试\scenes\main.tscn`
> Godot 会自动创建 `scenes` 文件夹。

### Step 3：给场景加点内容

现在场景是空的，我们来加几个节点，做一个最简单的界面。

**3.1 加一个背景色块**

1. 左上场景树，右键点 `Control` → **"添加子节点"**
2. 搜索 `ColorRect`，选中，点"创建"
3. 在右侧检查器里，找到 `color` 属性，点颜色方块，选一个深色（比如深蓝 `#1a1a2e`）
4. 在检查器顶部，找到 **"布局"** → 点 **"全矩形"**（让 ColorRect 铺满整个屏幕）

**3.2 加一个标题文字**

1. 右键 `Control` → **"添加子节点"**
2. 搜索 `Label`，选中，点"创建"
3. 检查器里 `text` 改成："我的第一个 Godot 场景！"
4. `theme_override_font_sizes/font_size` 改成 48
5. 布局 → **"居中顶部"**（让文字在屏幕上方居中）

**3.3 加一个按钮**

1. 右键 `Control` → **"添加子节点"**
2. 搜索 `Button`，选中，点"创建"
3. `text` 改成："点我试试"
4. 布局 → **"居中"**
5. `theme_override_font_sizes/font_size` 改成 24

### Step 4：保存场景

按 `Ctrl + S`

现在你的场景树应该是：

```
Control（根节点）
├── ColorRect（深色背景）
── Label（标题文字）
── Button（按钮）
```

---

## 六、第五步：给按钮写 C# 脚本（10 分钟）

现在按钮在那，但点了没反应。我们来写一个 C# 脚本，让按钮被点击时打印一句话。

### Step 1：创建脚本文件

1. 在场景树里，**右键点根节点 `Control`**
2. 选 **"附加脚本"**
3. 弹出窗口：
   - **语言**：选 **C#**
   - **路径**：默认是 `res://control.cs`，改成 `res://scripts/MainScene.cs`
   - 点 **"创建"**

Godot 会自动生成一个 `.cs` 文件并打开代码编辑器。

### Step 2：看懂自动生成的代码

你会看到类似这样的内容：

```csharp
using Godot;
using System;

public partial class MainScene : Control
{
    public override void _Ready()
    {
    }

    public override void _Process(double delta)
    {
    }
}
```

**逐行解释：**

| 代码 | 意思 |
|------|------|
| `using Godot;` | 引入 Godot 的功能库（就像"打开工具箱"） |
| `using System;` | 引入 C# 基础功能库 |
| `public partial class MainScene : Control` | 定义一个类叫 MainScene，它"继承"了 Control 节点的能力 |
| `public override void _Ready()` | **出生时执行一次**的函数。节点加载完成后自动调用 |
| `public override void _Process(double delta)` | **每帧都执行**的函数。用于动画、移动等持续更新的事 |

> 💡 **关键理解**：`_Ready` 和 `_Process` 是 Godot 的"生命周期函数"。
> - `_Ready` = 节点刚出生时执行一次（适合做初始化：找按钮、绑定事件）
> - `_Process` = 游戏每刷新一次画面就执行一次（适合做动画、计时）
> - 我们 Day 1 只需要用 `_Ready`

### Step 3：写按钮点击逻辑

把代码改成这样（删掉 `_Process`，在 `_Ready` 里加内容）：

```csharp
using Godot;
using System;

public partial class MainScene : Control
{
    public override void _Ready()
    {
        // 1. 找到按钮节点（通过场景树里的路径）
        var myButton = GetNode<Button>("Button");

        // 2. 把按钮的"被按下"信号，接到我们的函数上
        myButton.Pressed += OnButtonPressed;

        GD.Print("脚本已加载，按钮已绑定");
    }

    // 3. 自己写的函数：按钮被点击时执行
    private void OnButtonPressed()
    {
        GD.Print("你点了按钮！");
    }
}
```

**逐行解释：**

| 代码 | 意思 |
|------|------|
| `GetNode<Button>("Button")` | 在场景树里找叫 "Button" 的节点，把它当作 Button 类型 |
| `myButton.Pressed += OnButtonPressed` | 把按钮的 `Pressed` 信号（门铃）接到 `OnButtonPressed` 函数上 |
| `GD.Print("...")` | 在 Godot 的"输出"面板打印文字（调试用） |

### Step 4：保存脚本

按 `Ctrl + S`

---

## 七、第六步：设置主场景并运行（5 分钟）

### Step 1：设置主场景

现在项目还没有"主场景"（游戏启动时第一个显示的画面）。

1. 菜单：**项目** → **项目设置**
2. 点 **"应用"** 标签页（第一个）
3. 找到 **"主场景"**（Main Scene）
4. 点右边的 **"..."** 按钮
5. 选择 `res://scenes/main.tscn`
6. 点"关闭"

> 或者直接：在场景树面板顶部，点 **"场景"** → **"设置为运行场景"**

### Step 2：构建 + 运行

1. `Ctrl + Shift + B` 构建
2. `F5` 运行

你应该看到：
- 一个深色背景的游戏窗口
- 上方有"我的第一个 Godot 场景！"
- 中间有一个"点我试试"按钮
- 点按钮 → 看 Godot 编辑器底部的"输出"面板，会显示"你点了按钮！"

✅ 看到窗口 + 按钮能打印文字 = **从零创建成功！** 🎉

---

## 八、对比：你创建的文件 vs AI 预创建的文件

现在回头看我们的正式项目 `D:\游戏创作\基于放过哥的游戏中去\`，你会发现结构一模一样：

| 你刚创建的 | 正式项目里的 | 区别 |
|-----------|------------|------|
| `project.godot` | `project.godot` | 正式项目多了窗口大小 1920x1080、autoload 配置 |
| `从零创建测试.csproj` | `FangGuogeGame.csproj` | 只是名字不同，内容结构一样 |
| `scenes/main.tscn` | `scenes/main_menu/main_menu.tscn` | 正式项目多了更多节点（标题、副标题、两个按钮） |
| `scripts/MainScene.cs` | `scripts/main_menu/MainMenu.cs` | 正式项目多了"退出游戏"按钮和场景切换逻辑 |

**你刚才亲手做的每一步，就是 AI 之前帮你做的事。** 区别只是正式项目的文件内容更丰富（节点更多、逻辑更复杂），但创建方式完全一样。

---

## 九、知识回顾

做完这个练习，你应该能回答这些问题：

1. **`project.godot` 是什么？** → 项目的身份证，存窗口大小、主场景路径等配置
2. **`.csproj` 是什么？** → 告诉 .NET 用哪个版本编译 C# 代码
3. **`.tscn` 是什么？** → 场景文件，存节点树和布局
4. **`.cs` 脚本挂在哪？** → 挂在节点上，`_Ready` 出生时执行，`_Process` 每帧执行
5. **怎么找节点？** → `GetNode<类型>("节点名")`，按场景树里的路径找
6. **信号怎么用？** → `节点.信号名 += 函数名`，门铃响了函数就执行
7. **怎么设主场景？** → 项目设置 → 应用 → 主场景，或场景菜单 → 设置为运行场景

---

## 十、清理（可选）

这个测试项目只是用来学习的，做完可以删掉：
- 关闭 Godot 里打开的"从零创建测试"项目
- 删除文件夹 `D:\游戏创作\从零创建测试\`

然后回到正式项目 `D:\游戏创作\基于放过哥的游戏中去\`，继续 Day 1 的后续任务。
