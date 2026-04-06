namespace BGCS.Core
{
    using System.Collections.Generic;
    using System.Text;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Interfaces;
    using BGCS.CppAst.Model.Metadata;

    /// <summary>
    /// Defines the public class <c>NamespaceHelper</c>.
    /// </summary>
    public static class NamespaceHelper
    {
        /// <summary>
        /// Executes public operation <c>EnumerateNamespaces</c>.
        /// </summary>
        public static IEnumerable<CppNamespace> EnumerateNamespaces(this CppCompilation compilation)
        {
            Stack<CppNamespace> stack = new(compilation.Namespaces);
            while (stack.TryPop(out var ns))
            {
                yield return ns;
                foreach (var childNs in ns.Namespaces)
                {
                    stack.Push(childNs);
                }
            }
        }

        /// <summary>
        /// Returns computed data from <c>GetFullNamespace</c>.
        /// </summary>
        public static string GetFullNamespace(this CppNamespace ns, string sep)
        {
            StringBuilder sb = new();
            ICppContainer? current = ns;
            while (current is CppNamespace currentNs)
            {
                if (sb.Length > 0)
                {
                    sb.Insert(0, sep);
                }
                sb.Insert(0, currentNs.Name);
                current = currentNs.Parent;
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Defines the public class <c>CppClassExtensions</c>.
    /// </summary>
    public static class CppClassExtensions
    {
        /// <summary>
        /// Executes public operation <c>HasVirtualMembers</c>.
        /// </summary>
        public static bool HasVirtualMembers(this CppClass cls)
        {
            foreach (var destructor in cls.Destructors)
            {
                if (destructor.IsVirtual)
                {
                    return true;
                }
            }

            foreach (var constructor in cls.Constructors)
            {
                if (constructor.IsVirtual)
                {
                    return true;
                }
            }

            foreach (var function in cls.Functions)
            {
                if (function.IsVirtual)
                {
                    return true;
                }
            }

            foreach (var clsBase in cls.BaseTypes)
            {
                if (clsBase.Type is CppClass baseClass && baseClass.HasVirtualMembers())
                {
                    return true;
                }
            }

            return false;
        }
    }
}
