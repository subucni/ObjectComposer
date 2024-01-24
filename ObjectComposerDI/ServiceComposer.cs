
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;

namespace ObjectComposer.Services.Composer;

/// <summary>
/// Create object based on T (if T is interface) and implement T on object, and fill up with objects from DI container
/// </summary>
/// <typeparam name="T"></typeparam>
public class ServiceComposer<T> : IServiceComposer<T>
    where T : IEmittingService
{
    private readonly IServiceProvider _provider;
    private record PItem(PropertyInfo Prop, object Service);

    public T Implementation { get; protected set; }

    public ServiceComposer(IServiceProvider provider)
    {
        _provider = provider;
    }

    protected T Compose()
    {
        var tType = typeof(T);

        if (!tType.IsInterface)
        {
            throw new InvalidOperationException($"{tType.Name} should be interface");
        }

        var pItems = tType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsInterface)
            .Select(p => new PItem(p, _provider.GetRequiredService(p.PropertyType)));

        var typeBuilder = GetTypeBuilder(tType);

        foreach (var prop in pItems)
        {
            ComposeProperty(typeBuilder, prop);
        }

        var dynamicType = typeBuilder.CreateType();
        var newObject = (T)Activator.CreateInstance(dynamicType);

        foreach (var prop in newObject.GetType().GetProperties())
        {
            prop.SetValue(newObject, pItems.First(p => p.Prop.Name == prop.Name).Service);
        }

        return newObject;
    }

    private static TypeBuilder GetTypeBuilder(Type serviceType)
    {
        var assemblyName = new AssemblyName($"DynoSrv.{serviceType.Name}");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule($"{assemblyName.Name}Module");
        var typeBuilder = module.DefineType($"{serviceType.Name}Instance", TypeAttributes.Public);

        typeBuilder.AddInterfaceImplementation(serviceType);

        return typeBuilder;
    }

    private static void ComposeProperty(TypeBuilder tb, PItem pItem)
    {
        FieldBuilder field = tb.DefineField($"_{pItem.Prop.Name.ToLower()}", pItem.Prop.PropertyType, FieldAttributes.Private);
        PropertyBuilder pItemProp = tb.DefineProperty(pItem.Prop.Name, PropertyAttributes.HasDefault, pItem.Prop.PropertyType, null);

        MethodAttributes getSetAttr = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Virtual;
        MethodBuilder getter = tb.DefineMethod($"get_{pItem.Prop.Name}", getSetAttr, pItem.Prop.PropertyType, Type.EmptyTypes);
        MethodBuilder setter = tb.DefineMethod($"set_{pItem.Prop.Name}", getSetAttr, null, new Type[] { pItem.Prop.PropertyType });

        ILGenerator pItemGetIL = getter.GetILGenerator();
        ILGenerator pItemSetIL = setter.GetILGenerator();

        pItemGetIL.Emit(OpCodes.Ldarg_0);
        pItemGetIL.Emit(OpCodes.Ldfld, field);
        pItemGetIL.Emit(OpCodes.Ret);

        pItemSetIL.Emit(OpCodes.Ldarg_0);
        pItemSetIL.Emit(OpCodes.Ldarg_1);
        pItemSetIL.Emit(OpCodes.Stfld, field);
        pItemSetIL.Emit(OpCodes.Ret);

        pItemProp.SetGetMethod(getter);
        pItemProp.SetSetMethod(setter);
    }
}
