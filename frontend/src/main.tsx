import { ComponentInstance, css, FC } from "dreamland/core";
import "./style.css";
import { initDotnet, play, dotnetState } from "./dotnet";
import { loglisteners } from "./dotnet/log";
import { dumpWasmJit } from "./dotnet/jitbench";
import {
	downloadFabricMinecraftVersionToOpfs,
	isMinecraftVersionDownloaded,
} from "./minecraft";
import { OpfsExplorer } from "./fs";
import { Button, Icon } from "./ui/Button";
import { TextField } from "./ui/TextField";
import iconDns from "@ktibow/iconset-material-symbols/dns-outline";
import iconCode from "@ktibow/iconset-material-symbols/code";
import iconFolderOpen from "@ktibow/iconset-material-symbols/folder-open";
import iconPlayArrow from "@ktibow/iconset-material-symbols/play-arrow";

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
LogView.style = css`
	:scope {
		min-height: 0;
		flex: 1;
		font-family: var(--font-mono);

		padding: 0.5rem;
		background: var(--bg-sub);
		border-top: 0.1rem solid var(--surface4);
	}

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

function Logo(this: FC) {
	return <span>ikvmcraft</span>;
}
Logo.style = css`
	:scope {
		font-family: var(--font-display);
		font-size: 2rem;
	}
`;

function Runner(
	this: FC<
		{
			version: string;
			fabric: string;
		},
		{
			canvas: HTMLCanvasElement;
			fsOpen: boolean;
			loadState: string;
		}
	>
) {
	this.fsOpen = false;
	this.loadState = "Initializing runtime";

	let version = this.version;
	let fabric = this.fabric;
	this.cx.mount = async () => {
		await initDotnet(this.canvas);
		this.loadState = "Verifying assets";

		if (new URLSearchParams(location.search).has("bench"))
			console.debug("?bench: skipping the Minecraft download check");
		else {
			let verify = await isMinecraftVersionDownloaded(
				`${version}-fabric-${fabric}`,
				{
					verification: "size",
				}
			);
			console.log(verify);
			if (!verify) {
				this.loadState = "Downloading assets";
				await downloadFabricMinecraftVersionToOpfs(version, {
					loaderVersion: fabric,
				});
			} else {
				console.debug(`downlaodead ${version} fabric ${fabric} already`);
			}
		}

		this.loadState = "";

		await play(`${version}-fabric-${fabric}`);
	};

	let toFixed = (x: number) => x.toFixed(0);

	let threadDump = () => {
		(globalThis as any).wasm.Module.ccall("perform_thread_dump");
	};

	return (
		<div>
			<div class="header">
				<Logo />
				<div class="expand" />
				<div
					class="stats"
					class:hidden={use(this.loadState).map((x) => x !== "")}
				>
					<div class="label fps">fps:</div>
					<div class="stat fps">
						<span>
							{use(dotnetState.fps)} (
							{use(dotnetState.fpsAvg).map((x) => x.toFixed(2))})
						</span>
					</div>
					<div class="label memory">memory (mb):</div>
					<div class="stat memory">
						<span>
							gc {use(dotnetState.gcMemory).map(toFixed)}/
							{use(dotnetState.gcHeapMemory).map(toFixed)}
						</span>
						<span>native {use(dotnetState.nativeMemory).map(toFixed)}</span>
						<span>heap {use(dotnetState.heapMemory).map(toFixed)}</span>
						<span>wasm {use(dotnetState.wasmMemory).map(toFixed)}</span>
					</div>
				</div>
				<Button type="normal" icon="left" on:click={threadDump}>
					<Icon icon={iconDns} />
					Thread dump
				</Button>
				<Button type="normal" icon="left" on:click={dumpWasmJit}>
					<Icon icon={iconCode} />
					JIT dump
				</Button>
				<Button
					type="primary"
					icon="full"
					on:click={() => (this.fsOpen = !this.fsOpen)}
				>
					<Icon icon={iconFolderOpen} />
				</Button>
			</div>
			<div class="canvas">
				<div
					class="overlay not-ready"
					class:hidden={use(this.loadState).map((x) => x === "")}
				>
					{use(this.loadState)}
				</div>
				<canvas
					id="canvas"
					this={use(this.canvas)}
					on:contextmenu={(e: Event) => e.preventDefault()}
				/>
				<div class="overlay waiting">Waiting for game</div>
			</div>
			<LogView scrolling={true} />
			<OpfsExplorer open={use(this.fsOpen)} />
		</div>
	);
}
Runner.style = css`
	:scope {
		height: 100%;
		display: flex;
		flex-direction: column;

		background: var(--bg-sub);
	}

	.header {
		padding: 1rem;

		display: flex;
		gap: 1rem;
		align-items: center;

		background: var(--bg);
		border-bottom: 0.1rem solid var(--surface4);
	}

	.stats {
		display: grid;
		grid-auto-rows: max-content 1fr;
		grid-auto-columns: max-content 1fr;
		gap: 0.5rem 1rem;
	}
	.stats .label {
		grid-column: 1;
		justify-self: end;
	}
	.stats .stat {
		grid-column: 2;
	}
	.stats .fps {
		grid-row: 1;
	}
	.stats .memory {
		grid-row: 2;
	}

	.stats .stat.memory {
		display: flex;
		gap: 0.5rem;
	}

	.canvas {
		align-self: center;

		width: min(100%, calc(60dvh * 854 / 480));
		height: auto;
		aspect-ratio: 854 / 480;
		flex: none;
		min-width: 0;
		contain: size;

		display: grid;
		grid-template-areas: "canvas";
		isolation: isolate;
	}
	.canvas > * {
		grid-area: canvas;
	}
	.canvas .overlay {
		height: 100%;
		width: 100%;

		display: flex;
		align-items: center;
		justify-content: center;

		background: var(--surface1);
		font-family: var(--font-display);
		font-size: 4rem;
	}
	.canvas canvas {
		width: 100%;
		height: 100%;
		z-index: 0;
	}
	.canvas .not-ready {
		z-index: 1;
	}
	.canvas .waiting {
		z-index: -1;
	}

	.expand {
		flex: 1;
	}
	.hidden {
		visibility: hidden;
	}
`;

function Launcher(
	this: FC<
		{
			version: string;
			fabric: string;
			play: () => void;
		},
		{
			fsOpen: boolean;
		}
	>
) {
	this.fsOpen = false;
	let ver = this.version,
		fab = this.fabric;
	return (
		<div>
			<Logo />
			<p>This is a web port.</p>
			<div class="settings">
				<div>
					<span>Minecraft version: </span>
					<TextField value={use(this.version)} placeholder={ver} />
				</div>
				<div>
					<span>Fabric Loader version: </span>
					<TextField value={use(this.fabric)} placeholder={fab} />
				</div>
				<div class="options">
					<Button type="primary" icon="left" class="play" on:click={() => this.play()}>
						<Icon icon={iconPlayArrow} />
						Play
					</Button>
					<Button
						type="normal"
						icon="left"
						on:click={() => (this.fsOpen = !this.fsOpen)}
					>
						<Icon icon={iconFolderOpen} />
						Files
					</Button>
				</div>
			</div>
			<OpfsExplorer open={use(this.fsOpen)} />
		</div>
	);
}
Launcher.style = css`
	:scope {
		height: 100%;
		margin: auto;

		max-width: 50rem;
		padding: 1rem;
	}

	.settings {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.options {
		display: flex;
		flex-direction: row;
		gap: 0.5rem;
	}
	.options :global(.play) {
		flex: 1;
	}
`;

function App(
	this: FC<
		{},
		{
			version: string;
			fabric: string;
			runner: ComponentInstance<typeof Runner>;
		}
	>
) {
	this.version = "26.2";
	this.fabric = "0.19.5";

	let play = async () => {
		this.runner = (
			<Runner version={this.version} fabric={this.fabric} />
		) as ComponentInstance<typeof Runner>;
	};

	return (
		<div>
			<div class="launcher" class:hidden={use(this.runner).map((x) => !!x)}>
				<Launcher
					version={use(this.version)}
					fabric={use(this.fabric)}
					play={play}
				/>
			</div>
			<div>{use(this.runner)}</div>
		</div>
	);
}
App.style = css`
	:scope {
		position: relative;
		height: 100%;
		width: 100%;
		background: var(--bg);
	}

	.launcher {
		z-index: 1;
	}

	:scope > * {
		position: absolute;
		inset: 0;
	}

	.hidden {
		display: none;
	}
`;

document.querySelector("#app")!.replaceWith(<App />);
