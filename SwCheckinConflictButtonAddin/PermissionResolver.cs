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
                    "DomainFolder", "Parent", "文件夹");

            if (string.IsNullOrWhiteSpace(row.FolderPath) && folder != null)
            {
                row.FolderPath = FirstNonEmpty(
                    ReflectionValue.GetString(folder, "FullPath", "Path", "FolderPath", "DisplayPath", "全路径", "路径"),
                    BuildFolderPath(folder));
            }

            if (string.IsNullOrWhiteSpace(row.FolderPath) && bound != null)
            {
                string domain = ReflectionValue.GetString(bound,
                    "Domain", "DomainName", "Vault", "VaultName", "Library", "库", "所属域", "产品库", "项目库");
                string folderName = ReflectionValue.GetString(bound, "FolderName", "DirName", "目录名");
                row.FolderPath = JoinPath(domain, folderName);
            }

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
        }

        private static string ResolvePrefixed(object bound, bool read)
        {
            if (read)
            {
                return ReflectionValue.FormatPrivilege(ReflectionValue.Get(bound,
                    "FolderCanRead", "FolderRead", "FolderReadPrivilege", "ParentCanRead",
                    "文件夹读取", "目录读取"));
            }

            return ReflectionValue.FormatPrivilege(ReflectionValue.Get(bound,
                "FolderCanWrite", "FolderModify", "FolderWrite", "FolderWritePrivilege",
                "ParentCanWrite", "文件夹修改", "目录修改"));
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
                    "AllowRead", "IsRead", "读取", "可读", "读取权限")
                : ReflectionValue.Get(target,
                    "CanWrite", "CanModify", "HasWrite", "HasModify", "Write", "Writable",
                    "ModifyPrivilege", "WritePrivilege", "WriteAccess", "AllowWrite",
                    "IsWrite", "修改", "可写", "修改权限", "写入");

            if (value != null)
            {
                return ReflectionValue.FormatPrivilege(value);
            }

            object access = ReflectionValue.Get(target, "Access", "Privilege", "Permission", "AccessMask", "Rights");
            if (access is int mask)
            {
                return read
                    ? ReflectionValue.FormatPrivilege((mask & 1) != 0)
                    : ReflectionValue.FormatPrivilege((mask & 2) != 0 || (mask & 4) != 0);
            }

            string methodName = read ? "CanRead" : "CanWrite";
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase,
                null, Type.EmptyTypes, null);
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

        private static string BuildFolderPath(object folder)
        {
            string name = ReflectionValue.GetString(folder, "Name", "FolderName", "DisplayName", "名称");
            object parent = ReflectionValue.Get(folder, "Parent", "ParentFolder", "Owner");
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
