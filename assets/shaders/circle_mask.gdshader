shader_type canvas_item;

// 圆形遮罩：把方形头像裁成圆形，边缘做一点羽化过渡
// 原理：算每个像素到图片中心的距离，超过半径就丢弃（discard = 不画这个像素）
void fragment() {
	float d = distance(UV, vec2(0.5));
	float a = smoothstep(0.5, 0.482, d);
	if (a <= 0.0) {
		discard;
	}
	COLOR.a *= a;
}
