using System.Linq;
using org.objectweb.asm;
using org.objectweb.asm.tree;

internal class ThereAreNoBareClientsTransform(string name, ClassWriter writer) : ClassNode(Opcodes.ASM9)
{
	public static IkvmClassLoaderTransformer Transformer = () => {
		return ([Mappings.CurrentMappings.GetClass("net.minecraft.CrashReport").Name], typeof(ThereAreNoBareClientsTransform));
	};
	public override void visitEnd()
	{
		var build = methods.toArray().Cast<MethodNode>()
			.First(x => x.name == Mappings.CurrentMappings.GetClass("net.minecraft.CrashReport").GetMethod("getErrorComment", "()Ljava/lang/String;") && x.desc == "()Ljava/lang/String;");

		var c = new JavaCursor(build);
		c.EmitLdc("there are no bare clients");
		c.EmitAReturn();

		accept(writer);
	}
}
