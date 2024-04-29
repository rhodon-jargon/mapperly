using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Riok.Mapperly.Descriptors;
using static Riok.Mapperly.Emit.Syntax.SyntaxFactoryHelper;

namespace Riok.Mapperly.Symbols;

public class MemberPathSetterBuilder : IEquatable<MemberPathSetterBuilder>
{
    private MemberPathSetterBuilder(NonEmptyMemberPath memberPath, bool isMethod)
    {
        MemberPath = memberPath;
        IsMethod = isMethod;
    }

    /// <summary>
    /// Indicates whether this setter is an UnsafeAccessor for a property, i.e. target.SetValue(source.Value);
    /// False for standard properties, fields and UnsafeAccessor fields.
    /// </summary>
    public bool IsMethod { get; }

    public NonEmptyMemberPath MemberPath { get; }

    public static MemberPathSetterBuilder Build(MappingBuilderContext ctx, NonEmptyMemberPath memberPath)
    {
        // object path is the same as a getter
        var setterPath = MemberPathGetterBuilder.Build(ctx, memberPath.ObjectPath).ToList();
        // build the final member in the path and add it to the setter path
        var (member, isMethod) = BuildMemberSetter(ctx, memberPath.Member);
        setterPath.Add(member);

        return new MemberPathSetterBuilder(new NonEmptyMemberPath(memberPath.RootType, setterPath), isMethod);
    }

    private static (IMappableMember, bool) BuildMemberSetter(MappingBuilderContext ctx, IMappableMember member)
    {
        if (ctx.SymbolAccessor.IsDirectlyAccessible(member.MemberSymbol) && member.CanSetDirectly)
            return (member, false);

        if (member.MemberSymbol.Kind == SymbolKind.Field)
        {
            var unsafeFieldAccessor = ctx.UnsafeAccessorContext.GetOrBuildAccessor(
                UnsafeAccessorContext.UnsafeAccessorType.GetField,
                (IFieldSymbol)member.MemberSymbol
            );

            return (new MethodAccessorMember(member, unsafeFieldAccessor.MethodName), false);
        }

        var unsafeGetAccessor = ctx.UnsafeAccessorContext.GetOrBuildAccessor(
            UnsafeAccessorContext.UnsafeAccessorType.SetProperty,
            (IPropertySymbol)member.MemberSymbol
        );

        return (new MethodAccessorMember(member, unsafeGetAccessor.MethodName, methodRequiresParameter: true), true);
    }

    public ExpressionSyntax BuildAssignment(ExpressionSyntax? baseAccess, ExpressionSyntax valueToAssign, bool coalesceAssignment = false)
    {
        IEnumerable<IMappableMember> path = MemberPath.Path;

        if (baseAccess == null)
        {
            baseAccess = SyntaxFactory.IdentifierName(MemberPath.Path[0].Name);
            path = path.Skip(1);
        }

        var memberPath = path.Aggregate(baseAccess, (a, b) => b.BuildAccess(a));

        if (coalesceAssignment)
        {
            // cannot use coalesce assignment within a setter method invocation.
            Debug.Assert(!IsMethod);

            // target.Value ??= mappedValue;
            return CoalesceAssignment(memberPath, valueToAssign);
        }

        if (IsMethod)
        {
            // target.SetValue(source.Value);
            return Invocation(memberPath, valueToAssign);
        }

        // target.Value = source.Value;
        return Assignment(memberPath, valueToAssign);
    }

    public bool Equals(MemberPathSetterBuilder other) => IsMethod == other.IsMethod && MemberPath.Equals(other.MemberPath);

    bool IEquatable<MemberPathSetterBuilder>.Equals(MemberPathSetterBuilder? other) => other is not null && Equals(other);

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        return obj is MemberPathSetterBuilder setterBuilder && Equals(setterBuilder);
    }

    public override int GetHashCode() => HashCode.Combine(IsMethod, MemberPath);

    public static bool operator ==(MemberPathSetterBuilder? left, MemberPathSetterBuilder? right) => Equals(left, right);

    public static bool operator !=(MemberPathSetterBuilder? left, MemberPathSetterBuilder? right) => !Equals(left, right);
}
