using System.Linq;
using org.objectweb.asm;
using org.objectweb.asm.tree;

internal class NoopAssertThreadTransform(string name, ClassWriter writer) : ClassNode(Opcodes.ASM9)
{
	public static IkvmClassLoaderTransformer Transformer = () => {
		return ([Mappings.CurrentMappings.GetClass("com.mojang.blaze3d.systems.RenderSystem").Name], typeof(NoopAssertThreadTransform));
	};

	public override void visitEnd()
	{
		var assertThread = methods.toArray().Cast<MethodNode>()
			.First(x => x.name == "assertThread" && x.desc == "(Ljava/util/function/Supplier;)V");

		assertThread.instructions.clear();
		assertThread.instructions.add(new InsnNode(Opcodes.RETURN));
		assertThread.tryCatchBlocks?.clear();
		assertThread.localVariables?.clear();

		accept(writer);
	}
}
