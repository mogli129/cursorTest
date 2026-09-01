using System;
using System.Reflection;

namespace SwCheckinConflictButtonAddin
{
    internal static class PermissionResolver
    {
        public static void Fill(CadPermissionRow row)
        {
            object bound = row.DataBoundItem;
            object folder = bound == null
                ? null
                : ReflectionValue.Get(bound,
                    "Folder", "ParentFolder", "Directory", "Container", "OwnerFolder",
                    "DomainFolder", "Parent", "文件夹", "所属文件夹", "FolderInfo", "FolderObject");
            row.FolderObject = folder;

            row.DomainName = MapDomain(bound, folder);

            if (string.IsNullOrWhiteSpace(row.FolderPath) && folder != null)
            {
                row.FolderPath = FirstNonEmpty(
                    ReflectionValue.GetString(folder,
                        "FullPath", "Path", "FolderPath", "DisplayPath", "FullName", "全路径", "路径"),
                    BuildFolderPath(folder));
            }

            if (string.IsNullOrWhiteSpace(row.FolderPath) && bound != null)
            {
                row.FolderPath = FirstNonEmpty(
                    ReflectionValue.GetString(bound,
                        "FolderFullPath", "FolderPath", "FullPath", "Path", "ParentPath",
                        "ContainerPath", "Directory", "文件夹", "路径", "全路径", "Folder.FullPath"),
                    JoinPath(
                        ReflectionValue.GetString(bound, "FolderName", "DirName", "目录名"),
                        null));
            }

            row.FolderPath = EnsureDomainPrefix(row.FolderPath, row.DomainName);

            row.DocRead = Resolve(bound, true);
            row.DocModify = Resolve(bound, false);
            row.FolderRead = Resolve(folder, true);
            row.FolderModify = Resolve(folder, false);

            if (row.FolderRead == "未知" && bound != null)
            {
                row.FolderRead = ResolvePrefixed(bound, true);
            }

            if (row.FolderModify == "未知" && bound != null)
            {
                row.FolderModify = ResolvePrefixed(bound, false);
            }

            if (row.DocRead == "未知")
            {
                row.DocRead = PermissionServiceProbe.TryResolve(bound, true) ?? row.DocRead;
            }

            if (row.DocModify == "未知")
            {
                row.DocModify = PermissionServiceProbe.TryResolve(bound, false) ?? row.DocModify;
            }

            if (row.FolderRead == "未知")
            {
                row.FolderRead = PermissionServiceProbe.TryResolve(folder ?? bound, true) ?? row.FolderRead;
            }

            if (row.FolderModify == "未知")
            {
                row.FolderModify = PermissionServiceProbe.TryResolve(folder ?? bound, false) ?? row.FolderModify;
            }
        }

        private static string MapDomain(object bound, object folder)
        {
            string raw = FirstNonEmpty(
                bound == null ? null : ReflectionValue.GetString(bound,
                    "Domain", "DomainName", "DomainType", "Vault", "VaultName", "VaultType",
                    "Library", "LibraryType", "ContainerType", "ContextType", "ContextName",
                    "LibType", "所属域", "产品库", "项目库", "ContainerName"),
                folder == null ? null : ReflectionValue.GetString(folder,
                    "Domain", "DomainName", "DomainType", "Vault", "VaultName", "Library",
                    "ContainerType", "ContextType", "所属域"));
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string text = raw.Trim();
            if (text.IndexOf("项目", StringComparison.Ordinal) >= 0
                || text.Equals("Project", StringComparison.OrdinalIgnoreCase)
                || text.Equals("PJ", StringComparison.OrdinalIgnoreCase)
                || text.Equals("2", StringComparison.Ordinal))
            {
                return "项目库";
            }

            if (text.IndexOf("产品", StringComparison.Ordinal) >= 0
                || text.Equals("Product", StringComparison.OrdinalIgnoreCase)
                || text.Equals("PDM", StringComparison.OrdinalIgnoreCase)
                || text.Equals("PD", StringComparison.OrdinalIgnoreCase)
                || text.Equals("1", StringComparison.Ordinal)
                || text.Equals("0", StringComparison.Ordinal))
            {
                return "产品库";
            }

            return text;
        }

        private static string EnsureDomainPrefix(string path, string domain)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return domain ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(domain))
            {
                return path;
            }

            if (path.StartsWith(domain, StringComparison.Ordinal)
                || path.IndexOf("产品库", StringComparison.Ordinal) >= 0
                || path.IndexOf("项目库", StringComparison.Ordinal) >= 0)
            {
                return path;
            }

            return JoinPath(domain, path);
        }

        private static string ResolvePrefixed(object bound, bool read)
        {
            if (read)
            {
                return CoalescePrivilege(
                    ReflectionValue.Get(bound,
                        "FolderCanRead", "FolderRead", "FolderReadPrivilege", "ParentCanRead",
                        "文件夹读取", "目录读取", "FolderReadAccess"));
            }

            return CoalescePrivilege(
                ReflectionValue.Get(bound,
                    "FolderCanWrite", "FolderModify", "FolderWrite", "FolderWritePrivilege",
                    "ParentCanWrite", "文件夹修改", "目录修改", "FolderWriteAccess"));
        }

        private static string Resolve(object target, bool read)
        {
            if (target == null)
            {
                return "未知";
            }

            object value = read
                ? ReflectionValue.Get(target,
                    "CanRead", "HasRead", "Read", "Readable", "ReadPrivilege", "ReadAccess",
                    "AllowRead", "IsRead", "读取", "可读", "读取权限", "ReadRight", "HasReadAccess")
                : ReflectionValue.Get(target,
                    "CanWrite", "CanModify", "HasWrite", "HasModify", "Write", "Writable",
                    "ModifyPrivilege", "WritePrivilege", "WriteAccess", "AllowWrite",
                    "IsWrite", "修改", "可写", "修改权限", "写入", "WriteRight", "HasWriteAccess",
                    "CanUpdate");

            string formatted = CoalescePrivilege(value);
            if (formatted != "未知")
            {
                return formatted;
            }

            object access = ReflectionValue.Get(target,
                "Access", "Privilege", "Permission", "AccessMask", "Rights", "AccessRight", "PrivMask");
            if (access is int || access is long || access is short || access is byte)
            {
                long mask = Convert.ToInt64(access);
                return read
                    ? ReflectionValue.FormatPrivilege((mask & 1) != 0)
                    : ReflectionValue.FormatPrivilege((mask & 2) != 0 || (mask & 4) != 0);
            }

            string methodName = read ? "CanRead" : "CanWrite";
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase,
                null, Type.EmptyTypes, null);
            if (method == null && !read)
            {
                method = target.GetType().GetMethod("CanModify",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase,
                    null, Type.EmptyTypes, null);
            }

            if (method != null && (method.ReturnType == typeof(bool) || method.ReturnType == typeof(string)))
            {
                try
                {
                    return ReflectionValue.FormatPrivilege(method.Invoke(target, null));
                }
                catch
                {
                    // ignore
                }
            }

            return "未知";
        }

        private static string CoalescePrivilege(object value)
        {
            if (value == null)
            {
                return "未知";
            }

            return ReflectionValue.FormatPrivilege(value);
        }

        private static string BuildFolderPath(object folder)
        {
            string name = ReflectionValue.GetString(folder, "Name", "FolderName", "DisplayName", "名称");
            object parent = ReflectionValue.Get(folder, "Parent", "ParentFolder", "Owner", "Container");
            if (parent == null || ReferenceEquals(parent, folder))
            {
                return name;
            }

            string parentPath = FirstNonEmpty(
                ReflectionValue.GetString(parent, "FullPath", "Path"),
                BuildFolderPath(parent));
            return JoinPath(parentPath, name);
        }

        private static string JoinPath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left))
            {
                return right ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(right))
            {
                return left;
            }

            left = left.TrimEnd('\\', '/');
            right = right.TrimStart('\\', '/');
            return left + "\\" + right;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
