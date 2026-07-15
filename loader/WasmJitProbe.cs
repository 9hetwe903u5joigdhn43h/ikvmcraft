using System;
using System.Runtime.CompilerServices;

// ===== wasm-JIT exception-propagation repro (mirrors the MC RunningOnDifferentThreadException shape) =====
// MC crash: a JITted packet handler (class_2828$class_2829:method_11054 = Packet.handle) sits on the path
// between an interp THROW (PacketUtils.ensureRunningOnSameThread) and an interp CATCH
// (Connection.channelRead0 try/catch). The throw must propagate UP THROUGH the JITted frame via the
// cooperative EMIT_PENDING_EXC_CHECK unwind. jit86 (ISLAND=1, RESIDUAL=0) showed it escapes the catch.
// These kernels reproduce that deterministically, no MC: a JITted method makes a VIRTUAL call (the only
// call shape allowed to a non-JITted callee under RESIDUAL=0 — exactly like handle()->listener.handleX)
// to an interp method that throws, caught two frames up. Two variants: bare, and with an intermediate
// interp frame that has a finally (the one unwind path not provable correct by inspection).
internal abstract class EhBase { public abstract int Do (int x); }
internal sealed class EhThrower : EhBase {
    // interp (has `throw` -> OP_THROW unsupported -> JIT bails); returns x*2 otherwise.
    public override int Do (int x) { if (x == 42) throw new Exception ("boom-bare"); return x * 2; }
}
internal sealed class EhInner {
    [MethodImpl (MethodImplOptions.NoInlining)]
    public int Raise (int x) { if (x == 42) throw new Exception ("boom-finally"); return x * 3; }
}
internal abstract class EhBase2 { public abstract int Do (int x); }
internal sealed class EhMidHandler : EhBase2 {
    public static int sink;            // counts finally executions (must still run during unwind)
    EhInner inner = new EhInner ();
    // interp (has a finally clause -> num_clauses>0 -> JIT bails). Intermediate frame between the
    // throw (EhInner.Raise) and the catch (EhCatch2), reached across the JITted EhMid2 frame.
    public override int Do (int x) { try { return inner.Raise (x) + 1; } finally { sink++; } }
}

public static class WasmJitProbe {
    // ===== wasm-JIT vs interp+jiterpreter micro-bench (MC-like integer kernels) =====
    // Two scenarios, each with identical "Jit" (env-targeted -> wasm-JIT) and "Int" (interp+jiterp)
    // variants: (1) call-dense — an outer loop calling a leaf helper per iteration, like per-block
    // getBlockState; the jiterpreter bails at each call, the wasm-JIT lowers them to call_indirect.
    // (2) pure-compute — a hot loop with no calls, where the jiterpreter already traces the whole
    // loop, to measure raw codegen quality + per-call/instantiate overhead.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int BHJit(int x, int y, int z) { int h = x * 374761393 + y * 668265263 + z * 1274126177; h = (h ^ (h >> 13)) * 1274126177; return h ^ (h >> 16); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int BHInt(int x, int y, int z) { int h = x * 374761393 + y * 668265263 + z * 1274126177; h = (h ^ (h >> 13)) * 1274126177; return h ^ (h >> 16); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int TickJit(int seed, int n) { int acc = 0; for (int i = 0; i < n; i++) { int x = (seed + i) & 1023; int y = (seed * 3 + i) & 255; int z = (seed * 7 + i) & 1023; acc += BHJit(x, y, z); acc ^= BHJit(z, x, y); } return acc; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int TickInt(int seed, int n) { int acc = 0; for (int i = 0; i < n; i++) { int x = (seed + i) & 1023; int y = (seed * 3 + i) & 255; int z = (seed * 7 + i) & 1023; acc += BHInt(x, y, z); acc ^= BHInt(z, x, y); } return acc; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int CompJit(int seed, int n) { int h = seed; for (int i = 0; i < n; i++) { h = h * 1664525 + 1013904223; h ^= h >> 15; h += i; } return h; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int CompInt(int seed, int n) { int h = seed; for (int i = 0; i < n; i++) { h = h * 1664525 + 1013904223; h ^= h >> 15; h += i; } return h; }
    // polymorphic dispatch (the jiterpreter can't trace through virtual calls; the wasm-JIT lowers
    // them to call_indirect through the vtable). Two receiver types so the call sites really dispatch.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int PolyJit(Shape a, Shape b, int n) { int acc = 0; for (int i = 0; i < n; i++) { acc += a.Area(); acc += b.Area(); } return acc; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int PolyInt(Shape a, Shape b, int n) { int acc = 0; for (int i = 0; i < n; i++) { acc += a.Area(); acc += b.Area(); } return acc; }
    // virtual dispatch WITH a param (Scaled(int)) — exercises the generalized vcall lowering (args + functype)
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int PolyJit2(Shape a, Shape b, int n, int k) { int acc = 0; for (int i = 0; i < n; i++) { acc += a.Scaled(k); acc += b.Scaled(k); } return acc; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int PolyInt2(Shape a, Shape b, int n, int k) { int acc = 0; for (int i = 0; i < n; i++) { acc += a.Scaled(k); acc += b.Scaled(k); } return acc; }
    // INTERFACE (IMT) dispatch — Noise() resolves through the IMT; the JIT's resolve-based vcall
    // handles it the same as a class virtual (mono_object_get_virtual_method_internal resolves it).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int PolyJit3(INoise a, INoise b, int n) { int acc = 0; for (int i = 0; i < n; i++) { acc += a.Noise(); acc += b.Noise(); } return acc; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int PolyInt3(INoise a, INoise b, int n) { int acc = 0; for (int i = 0; i < n; i++) { acc += a.Noise(); acc += b.Noise(); } return acc; }

    // EH repro JIT targets: each makes ONE virtual call (to a non-JITted interp callee that throws),
    // then +1. Under RESIDUAL=0 they JIT cleanly (the virtual call uses the always-allowed vcall path),
    // so they become a JITted frame on the exception-propagation path. NoInlining keeps them as real
    // frames (the JIT targets the method, not an inlined copy in the caller).
    [MethodImpl (MethodImplOptions.NoInlining)] static int EhMid (EhBase t, int x) { return t.Do (x) + 1; }
    [MethodImpl (MethodImplOptions.NoInlining)] static int EhCatch (EhBase t, int x) { try { return EhMid (t, x); } catch (Exception) { return -1; } }
    [MethodImpl (MethodImplOptions.NoInlining)] static int EhMid2 (EhBase2 t, int x) { return t.Do (x) + 1; }
    [MethodImpl (MethodImplOptions.NoInlining)] static int EhCatch2 (EhBase2 t, int x) { try { return EhMid2 (t, x); } catch (Exception) { return -2; } }

    // Deterministic in-runtime repro of the ISLAND=1 RunningOnDifferentThreadException escape.
    // Warms EhMid/EhMid2 past the AUTO-JIT threshold (non-throwing input), then drives the throw path
    // and checks the exception is delivered to the interp catch across the JITted frame.
    // OP_THROW execution test: GuardThrow is a JIT target whose cold branch THROWS a passed-in exception
    // (OP_THROW, no newobj so no ctor-cascade bail). CatchGuard (interp, try/catch) calls it directly.
    [MethodImpl (MethodImplOptions.NoInlining)] static int GuardThrow (int x, Exception e) { if (x < 0) throw e; return x + 1; }
    [MethodImpl (MethodImplOptions.NoInlining)] static int CatchGuard (int x, Exception e) { try { return GuardThrow (x, e); } catch (Exception) { return -7; } }

    public static void WasmJitEhTest ()
    {
        Console.WriteLine ("[ehtest] start");
        var t1 = new EhThrower ();
        var t2 = new EhMidHandler ();
        // Warm up so MONO_WASM_JIT_AUTO compiles EhMid/EhMid2 (threshold 2000). x=1 => no throw.
        int warm = 0;
        for (int i = 0; i < 4000; i++) { warm += EhMid (t1, 1); warm += EhMid2 (t2, 1); }
        Console.WriteLine ($"[ehtest] warmed chk={warm} (EhMid/EhMid2 should be WASM_JIT_REGISTERED above)");

        // --- Test 1: bare propagation through a JITted frame ---
        int r1n = -999, r1t = -999; bool esc1 = false;
        try { r1n = EhCatch (t1, 1); } catch (Exception ex) { Console.WriteLine ($"[ehtest] T1 no-throw UNEXPECTED escape: {ex.GetType ().Name}"); }
        try { r1t = EhCatch (t1, 42); } catch (Exception ex) { esc1 = true; Console.WriteLine ($"[ehtest] T1 throw ESCAPED past inner catch -> {ex.GetType ().Name}: {ex.Message}"); }
        Console.WriteLine ($"[ehtest] T1 bare:     no-throw={r1n} (exp 3)  throw={r1t} (exp -1)  escaped={esc1}  => {(r1n == 3 && r1t == -1 && !esc1 ? "PASS" : "FAIL")}");

        // --- Test 2: intermediate interp frame WITH a finally ---
        EhMidHandler.sink = 0;
        int r2n = -999, r2t = -999; bool esc2 = false;
        try { r2n = EhCatch2 (t2, 1); } catch (Exception ex) { Console.WriteLine ($"[ehtest] T2 no-throw UNEXPECTED escape: {ex.GetType ().Name}"); }
        try { r2t = EhCatch2 (t2, 42); } catch (Exception ex) { esc2 = true; Console.WriteLine ($"[ehtest] T2 throw ESCAPED past inner catch -> {ex.GetType ().Name}: {ex.Message}"); }
        Console.WriteLine ($"[ehtest] T2 finally:  no-throw={r2n} (exp 5)  throw={r2t} (exp -2)  finally-ran={EhMidHandler.sink} (exp 2)  escaped={esc2}  => {(r2n == 5 && r2t == -2 && EhMidHandler.sink == 2 && !esc2 ? "PASS" : "FAIL")}");

        // --- Test 3: OP_THROW — a JITted method itself throws (passed-in exc), caught in interp ---
        var boom = new Exception ("boom-opthrow");
        int warm3 = 0;
        for (int i = 0; i < 4000; i++) warm3 += GuardThrow (1, boom);   // AUTO-JIT GuardThrow (OP_THROW in the cold branch)
        int r3n = -999, r3t = -999; bool esc3 = false;
        try { r3n = CatchGuard (5, boom); } catch (Exception ex) { Console.WriteLine ($"[ehtest] T3 no-throw UNEXPECTED escape: {ex.GetType ().Name}"); }
        try { r3t = CatchGuard (-1, boom); } catch (Exception ex) { esc3 = true; Console.WriteLine ($"[ehtest] T3 throw ESCAPED past catch -> {ex.GetType ().Name}: {ex.Message}"); }
        Console.WriteLine ($"[ehtest] T3 OP_THROW: no-throw={r3n} (exp 6)  throw={r3t} (exp -7)  escaped={esc3}  (GuardThrow should be WASM_JIT_REGISTERED) => {(r3n == 6 && r3t == -7 && !esc3 ? "PASS" : "FAIL")}");
        Console.WriteLine ("[ehtest] done");
    }

    public static void WasmJitProbeRun()
    {
        const int ITERS = 20000, REPS = 200;
        // Warm up: JIT the leaf helper + compute kernels FIRST (so TickJit's compile can resolve
        // BHJit's f-slot), and let the jiterpreter trace the interp variants.
        int wj = 0;
        for (int w = 0; w < 400; w++) { wj += BHJit(w, w + 1, w + 2); wj += CompJit(w, 2000); wj += CompInt(w, 2000); wj += BHInt(w, w + 1, w + 2); }
        for (int w = 0; w < 400; w++) { wj += TickJit(w, 2000); wj += TickInt(w, 2000); }
        // poly warmup: PolyInt FIRST each iter so the receivers' vtable slots are initialized (in
        // interp) before PolyJit's compiled code dispatches through them (its cold init path traps).
        Shape sq = new Sq { s = 7 }, rc = new Rc { w = 3, h = 5 };
        INoise wind = new Wind(), rain = new Rain();
        for (int w = 0; w < 400; w++) { wj += PolyInt(sq, rc, 2000); wj += PolyJit(sq, rc, 2000); wj += PolyInt2(sq, rc, 2000, 3); wj += PolyJit2(sq, rc, 2000, 3); wj += PolyInt3(wind, rain, 2000); wj += PolyJit3(wind, rain, 2000); }
        // Best-of-N per kernel: take the MIN time across TRIALS interleaved trials. The min is the
        // least-contended run (closest to true compute cost) — robust to the noisy environment
        // (this runs during PreInit alongside IKVM/MC init, GC, V8 wasm re-tiering, thread contention),
        // where single-shot absolute times swing 2x+ even though the interp baseline is fixed wasm.
        const int TRIALS = 9;
        long tickJit = long.MaxValue, tickInt = long.MaxValue, compJit = long.MaxValue, compInt = long.MaxValue, polyJit = long.MaxValue, polyInt = long.MaxValue, poly2Jit = long.MaxValue, poly2Int = long.MaxValue, poly3Jit = long.MaxValue, poly3Int = long.MaxValue;
        int a = 0, b = 0, c = 0, d = 0, e = 0, f = 0, g = 0, h = 0, ij = 0, ik = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int t = 0; t < TRIALS; t++) {
            sw.Restart(); a = 0; for (int r = 0; r < REPS; r++) a += TickJit(r, ITERS); sw.Stop(); tickJit = System.Math.Min(tickJit, sw.ElapsedMilliseconds);
            sw.Restart(); b = 0; for (int r = 0; r < REPS; r++) b += TickInt(r, ITERS); sw.Stop(); tickInt = System.Math.Min(tickInt, sw.ElapsedMilliseconds);
            sw.Restart(); c = 0; for (int r = 0; r < REPS; r++) c += CompJit(r, ITERS); sw.Stop(); compJit = System.Math.Min(compJit, sw.ElapsedMilliseconds);
            sw.Restart(); d = 0; for (int r = 0; r < REPS; r++) d += CompInt(r, ITERS); sw.Stop(); compInt = System.Math.Min(compInt, sw.ElapsedMilliseconds);
            sw.Restart(); e = 0; for (int r = 0; r < REPS; r++) e += PolyJit(sq, rc, ITERS); sw.Stop(); polyJit = System.Math.Min(polyJit, sw.ElapsedMilliseconds);
            sw.Restart(); f = 0; for (int r = 0; r < REPS; r++) f += PolyInt(sq, rc, ITERS); sw.Stop(); polyInt = System.Math.Min(polyInt, sw.ElapsedMilliseconds);
            sw.Restart(); g = 0; for (int r = 0; r < REPS; r++) g += PolyJit2(sq, rc, ITERS, 3); sw.Stop(); poly2Jit = System.Math.Min(poly2Jit, sw.ElapsedMilliseconds);
            sw.Restart(); h = 0; for (int r = 0; r < REPS; r++) h += PolyInt2(sq, rc, ITERS, 3); sw.Stop(); poly2Int = System.Math.Min(poly2Int, sw.ElapsedMilliseconds);
            sw.Restart(); ij = 0; for (int r = 0; r < REPS; r++) ij += PolyJit3(wind, rain, ITERS); sw.Stop(); poly3Jit = System.Math.Min(poly3Jit, sw.ElapsedMilliseconds);
            sw.Restart(); ik = 0; for (int r = 0; r < REPS; r++) ik += PolyInt3(wind, rain, ITERS); sw.Stop(); poly3Int = System.Math.Min(poly3Int, sw.ElapsedMilliseconds);
        }
        Console.WriteLine($"[bench] poly-check jit1={PolyJit(sq, rc, 1)} int1={PolyInt(sq, rc, 1)} (expect 64/64)  poly2-check jit1={PolyJit2(sq, rc, 1, 3)} int1={PolyInt2(sq, rc, 1, 3)} (expect 192/192)  iface-check jit1={PolyJit3(wind, rain, 1)} int1={PolyInt3(wind, rain, 1)} (expect 18/18)");
        Console.WriteLine($"[bench] (best-of-{TRIALS}) call-dense: jit={tickJit}ms interp={tickInt}ms ({(double)tickInt / (tickJit == 0 ? 1 : tickJit):F2}x)  pure-compute: jit={compJit}ms interp={compInt}ms ({(double)compInt / (compJit == 0 ? 1 : compJit):F2}x)  virtual-dispatch: jit={polyJit}ms interp={polyInt}ms ({(double)polyInt / (polyJit == 0 ? 1 : polyJit):F2}x)  virtual-param: jit={poly2Jit}ms interp={poly2Int}ms ({(double)poly2Int / (poly2Jit == 0 ? 1 : poly2Jit):F2}x)  interface: jit={poly3Jit}ms interp={poly3Int}ms ({(double)poly3Int / (poly3Jit == 0 ? 1 : poly3Jit):F2}x)  chk={wj},{a},{b},{c},{d},{e},{f},{g},{h},{ij},{ik}");
    }
}
// wasm-JIT virtual-dispatch bench receivers (two concrete types so the call sites dispatch)
internal abstract class Shape { public abstract int Area(); public abstract int Scaled(int k); }
internal sealed class Sq : Shape { public int s; public override int Area() => s * s; public override int Scaled(int k) => s * s * k; }
internal sealed class Rc : Shape { public int w, h; public override int Area() => w * h; public override int Scaled(int k) => w * h * k; }

// interface (IMT) dispatch receivers — Noise() dispatches through the IMT, not the vtable
internal interface INoise { int Noise(); }
internal sealed class Wind : INoise { public int Noise() => 7; }
internal sealed class Rain : INoise { public int Noise() => 11; }
