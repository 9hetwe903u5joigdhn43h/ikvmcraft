using System.Linq;
using org.objectweb.asm;
using org.objectweb.asm.tree;

internal class FixCrashReportTransform(string name, ClassWriter writer) : ClassNode(Opcodes.ASM9)
{
	public static IkvmClassLoaderTransformer Transformer = () => {
		return ([Mappings.CurrentMappings.GetClass("net.minecraft.CrashReportCategory").Name], typeof(FixCrashReportTransform));
	};

	public override void visitEnd()
	{
		var desc = "(Ljava/lang/StackTraceElement;Ljava/lang/StackTraceElement;)Z";
		var build = methods.toArray().Cast<MethodNode>()
			.First(x => x.name == Mappings.CurrentMappings.GetClass("net.minecraft.CrashReportCategory").GetMethod("validateStackTrace", desc) && x.desc == desc);

		var c = new JavaCursor(build);
		c.GotoNext(
			JavaMoveType.After,
			JavaMatch.ALoad(),
			JavaMatch.InvokeVirtual("java/lang/StackTraceElement", "getFileName", "()Ljava/lang/String;"),
			JavaMatch.ALoad(),
			JavaMatch.InvokeVirtual("java/lang/StackTraceElement", "getFileName", "()Ljava/lang/String;"));

		// 54: invokevirtual #175                // Method java/lang/String.equals:(Ljava/lang/Object;)Z
		c.Remove();
		c.EmitMethod(Opcodes.INVOKESTATIC, "java/util/Objects", "equals", "(Ljava/lang/Object;Ljava/lang/Object;)Z");

		accept(writer);
	}
}
