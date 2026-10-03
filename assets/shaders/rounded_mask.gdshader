shader_type canvas_item;

// 圆角矩形遮罩：把方形图片裁成"圆角矩形"（微信头像/照片那种圆角）
// 原理：算每个像素到"圆角矩形"边界的距离（SDF），超出就丢弃。
// rect_size / radius 由脚本在设置材质时传入（单位 = 图像像素）。
uniform vec2 rect_size = vec2(100.0, 100.0);
uniform float radius = 12.0;

void fragment() {
	vec2 half_size = rect_size * 0.5;
	vec2 p = abs(UV * rect_size - half_size) - (half_size - vec2(radius));
	float d = length(max(p, vec2(0.0))) + min(max(p.x, p.y), 0.0) - radius;

	float a = 1.0 - smoothstep(-1.0, 1.0, d);
	if (a <= 0.0) {
		discard;
	}
	COLOR.a *= a;
}
