using System.Linq;
using org.objectweb.asm;
using org.objectweb.asm.tree;

internal class NoopCheckAllocatedTransform(string name, ClassWriter writer) : ClassNode(Opcodes.ASM9)
{
	public static IkvmClassLoaderTransformer Transformer = () => {
		return ([Mappings.CurrentMappings.GetClass("com.mojang.blaze3d.platform.NativeImage").Name], typeof(NoopCheckAllocatedTransform));
	};

	public override void visitEnd()
	{
		var name = Mappings.CurrentMappings.GetClass("com.mojang.blaze3d.platform.NativeImage").GetMethod("checkAllocated", "()V");
		var checkAllocated = methods.toArray().Cast<MethodNode>().First(x => x.name == name && x.desc == "()V");

		checkAllocated.instructions.clear();
		checkAllocated.instructions.add(new InsnNode(Opcodes.RETURN));
		checkAllocated.tryCatchBlocks?.clear();
		checkAllocated.localVariables?.clear();

		accept(writer);
	}
}
