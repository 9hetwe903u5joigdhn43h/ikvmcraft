/*
 * WebGPU availability check.
 *
 * The renderer (wink) presents through WebGPU, and falls over if there is no
 * adapter. Without this check the failure only shows up after Minecraft has
 * booted, as a window-creation crash ("No supported graphics backend was
 * found"), which says nothing about the actual cause. Probing first lets the
 * launcher show the browser setting that has to change -- on Linux Chromium
 * that is Vulkan, which is off by default -- and avoids downloading the game
 * only to fail at the last step.
 */

export class WebGpuUnavailableError extends Error {
	constructor(message: string) {
		super(message);
		this.name = "WebGpuUnavailableError";
	}
}

export interface WebGpuProbe {
	adapter: any;
	/** The request options that produced `adapter`, reused by wink. */
	options: any;
	fallback: boolean;
}

// Tried in order. `powerPreference` is wink's default; a bare request helps
// when a browser refuses the discrete GPU; compatibility mode and the software
// fallback adapter are last resorts so a machine with a half-working GPU stack
// still gets a chance to start.
const ADAPTER_OPTIONS: any[] = [
	{ powerPreference: "high-performance" },
	{},
	{ featureLevel: "compatibility" },
	{ forceFallbackAdapter: true },
];

export async function probeWebGpu(): Promise<WebGpuProbe> {
	const gpu = (navigator as any).gpu;
	if (!gpu || typeof gpu.requestAdapter !== "function") {
		throw new WebGpuUnavailableError(missingGpuMessage());
	}

	for (const options of ADAPTER_OPTIONS) {
		let adapter: any;
		try {
			adapter = await gpu.requestAdapter(options);
		} catch (error) {
			console.warn("[webgpu] requestAdapter failed", options, error);
			continue;
		}
		if (!adapter) continue;

		const fallback =
			options.forceFallbackAdapter === true ||
			adapter.isFallbackAdapter === true;
		const info = adapter.info ?? {};
		const name = [info.vendor, info.architecture]
			.filter((part: unknown) => typeof part === "string" && part.length > 0)
			.join(" ");
		console.info(
			`[webgpu] adapter: ${name || "unknown"}${info.description ? ` (${info.description})` : ""}${fallback ? " [fallback]" : ""}`
		);
		if (fallback) {
			console.warn(
				"[webgpu] no hardware adapter is available; using a software fallback. The game will start, but expect very low frame rates."
			);
		}
		return { adapter, options, fallback };
	}

	throw new WebGpuUnavailableError(noAdapterMessage());
}

function missingGpuMessage(): string {
	return [
		"ikvmcraft renders with WebGPU, but this browser does not expose it (navigator.gpu is missing).",
		"",
		"Use Chrome or Edge 113+ (on Linux, WebGPU needs Vulkan enabled), Safari 18+, or a Firefox build with WebGPU enabled.",
		"WebGPU also needs a secure page: open the game over https:// or http://localhost.",
	].join("\n");
}

function noAdapterMessage(): string {
	const ua = navigator.userAgent;
	const linux = /Linux/.test(ua) && !/Android/.test(ua);
	const chromium = /(Chrome|Chromium)\//.test(ua);
	const firefox = /Firefox\//.test(ua);

	const lines = [
		"ikvmcraft renders with WebGPU, but the browser did not provide a GPU adapter.",
		"",
	];

	if (chromium && linux) {
		lines.push(
			"Chromium/Edge on Linux only offers WebGPU when Vulkan is enabled:",
			"  1. Open chrome://flags/#enable-vulkan, set it to Enabled (or start the browser with --enable-features=Vulkan).",
			"  2. Restart the browser completely, then press Retry.",
			"  3. chrome://gpu should list Vulkan and WebGPU as enabled, with hardware acceleration on."
		);
	} else if (firefox) {
		lines.push(
			"Firefox may have WebGPU disabled or unsupported on this platform:",
			"  1. Check about:config for dom.webgpu.enabled = true (Linux support is limited).",
			"  2. Make sure hardware acceleration is enabled and the GPU driver is up to date.",
			"  3. If it still fails, try Chrome or Edge."
		);
	} else {
		lines.push(
			"Make sure hardware acceleration is enabled in the browser, the GPU driver is up to date, and the browser is not running with GPU access disabled."
		);
	}

	lines.push(
		"",
		"A remote or headless session may not be able to reach the GPU; run the browser on the machine with the display."
	);
	return lines.join("\n");
}
