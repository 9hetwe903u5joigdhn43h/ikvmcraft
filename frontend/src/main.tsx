import { css, FC } from "dreamland/core";
import "./style.css";
import { initDotnet, play, dotnetState } from "./dotnet";
import { loglisteners } from "./dotnet/log";
import { dumpWasmJit } from "./dotnet/jitbench";
import {
	downloadFabricMinecraftVersionToOpfs,
	isMinecraftVersionDownloaded,
} from "./minecraft";
import { OpfsExplorer } from "./fs";

function LogView(this: FC<{ scrolling: boolean }>) {
	const create = (color: string, log: string) => {
		const el = document.createElement("div");
		el.classList.add("log");
		el.innerText = log;
		el.style.color = color;
		return el;
	};

	this.cx.mount = () => {
		const logroot = this.root as HTMLElement;
		const frag = document.createDocumentFragment();

		loglisteners.push((x) => frag.append(create(x.color, x.log)));
		setInterval(() => {
			if (frag.children.length > 0) {
				logroot.appendChild(frag);
				logroot.scrollTop = logroot.scrollHeight;
			}
		}, 50);
	};

	return (
		<div
			class="component-log"
			style={this.scrolling ? "overflow: auto" : "overflow: hidden"}
		/>
	);
}
LogView.css = `
	min-height: 0;
	flex: 1;
	font-family: var(--font-mono);

	::-webkit-scrollbar {
		width: 10px;
	}
	::-webkit-scrollbar-track {
		background: var(--surface3);
	}
	::-webkit-scrollbar-thumb {
		background: var(--surface6);
	}
`;

function App(
	this: FC<
		{},
		{
			canvas: HTMLCanvasElement;
			fsOpen: boolean;
			ready: boolean;
		}
	>
) {
	this.fsOpen = false;
	this.ready = false;

	let version = "1.16.5";
	let fabric = "0.19.5";
	this.cx.mount = async () => {
		if (new URLSearchParams(location.search).has("bench"))
			console.debug("?bench: skipping the Minecraft download check");
		else {
			let verify = await isMinecraftVersionDownloaded(`${version}-fabric-${fabric}`, {
				verification: "size",
			});
			console.log(verify);
			if (!verify)
				await downloadFabricMinecraftVersionToOpfs(version, {
					loaderVersion: fabric,
				});
			else console.debug(`downlaodead ${version} fabric ${fabric} already`);
		}

		await initDotnet(this.canvas);
		this.ready = true;
	};

	let start = async () => {
		this.ready = false;
		await play(`${version}-fabric-${fabric}`);
	}

	let toFixed = (x: number) => x.toFixed(0);

	return (
		<div>
			<canvas
				id="canvas"
				class="canvas"
				this={use(this.canvas)}
				on:contextmenu={(e: Event) => e.preventDefault()}
			/>
			<div class="debug">
				<b>
					fps: {use(dotnetState.fps)} (
					{use(dotnetState.fpsAvg).map((x) => x.toFixed(2))})
				</b>
				<button disabled={use(this.ready).not()} on:click={start}>Play</button>
				<button
					on:click={() =>
						(globalThis as any).wasm.Module.ccall("perform_thread_dump")
					}
				>
					Thread dump
				</button>
				<button on:click={() => dumpWasmJit()}>JIT dump</button>
				<button on:click={() => (this.fsOpen = !this.fsOpen)}>Files</button>
				<span>
					memory: gc {use(dotnetState.gcMemory).map(toFixed)}/
					{use(dotnetState.gcHeapMemory).map(toFixed)} · native{" "}
					{use(dotnetState.nativeMemory).map(toFixed)} · heap{" "}
					{use(dotnetState.heapMemory).map(toFixed)} · wasm{" "}
					{use(dotnetState.wasmMemory).map(toFixed)} (mb)
				</span>
			</div>
			<LogView scrolling={true} />
			<OpfsExplorer open={use(this.fsOpen)} />
		</div>
	);
}
App.style = css`
	:scope {
		height: 100%;
		display: flex;
		flex-direction: column;
		align-items: center;
	}

	:global(.component-log) {
		font-family: monospace;
		flex: 1;
		align-self: stretch;
		background: #eee;
	}

	.debug {
		display: flex;
		gap: 1rem;
	}
`;

document.querySelector("#app")!.replaceWith(<App />);
