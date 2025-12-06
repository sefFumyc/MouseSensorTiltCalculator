# Mouse Sensor Tilt Calculator / 鼠标传感器倾斜角计算工具

**Mouse Sensor Tilt Angle Testing Tool designed for the Rawaccel angle correction feature**
**为Rawaccel角度修正功能设计的鼠标传感器倾斜角测试工具**

## Features / 功能

* **Raw Input Capture**: Bypasses Windows pointer ballistics for 1:1 sensor data.
    * **原始输入捕获**：绕过 Windows 指针曲线，获取 1:1 传感器数据。
* **Y-axis Visual Shielding**: Simulates "Angle Snapping" visually to isolate horizontal tracking.
    * **Y轴视觉屏蔽**：视觉上屏蔽垂直抖动，专注于水平跟枪分析。
* **Visual Trajectory Analysis**: Visualizes your mouse path to diagnose sensor tilt vs. wrist mechanics.
    * **轨迹分析**：通过轨迹连线，直观区分是握姿倾斜还是手腕生理弧线。
* **Deviation  Calculation**: Provides the exact `Rotation` value needed for RawAccel.
    * **偏移计算**：直接给出 RawAccel 所需的 `Rotation` 修正数值。

## How to Use / 使用说明

1.  **Adjust**: Use arrow keys ↑ and ↓ to adjust sensitivity and use ← and → to adjust target speed.
    * **调整**：使用 ↑ ↓ 方向键调整灵敏度和 ← → 方向键调整红球速度。
2.  **Test**: **Hold Left Mouse Button** and track the red ball.It allows the left button to be released and pressed again during the process.
    * **测试**：**按住鼠标左键**并跟随红球移动。中途允许松开左键再重新按下。
3.  **Result**: Press 'Space' or 'ESC' to view the calculated dip angle and trajectory graph, as well as the offset calculation results after filtering and cancellation.
    * **结果**：按空格或 ESC 查看计算出的倾角和轨迹图，以及滤波抵消后的偏移计算结果。


