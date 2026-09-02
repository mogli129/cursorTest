using System;
using System.Collections;
using System.Collections.Generic;

namespace SwCheckinConflictButtonAddin
{
    internal sealed class PlmCadDoc
    {
        public string Number { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
        public string DocOid { get; set; }
        public string DocOtype { get; set; }
        public string FolderOid { get; set; }
        public string FolderOtype { get; set; }
        public string FolderPath { get; set; }
        public string ContainerType { get; set; }
        public string ContainerName { get; set; }
        public string CabinetOid { get; set; }
        public string CabinetOtype { get; set; }
    }

    internal sealed class PlmAccess
    {
        public string ObjectOid { get; set; }
        public string Missing { get; set; }
        public bool? Authorized { get; set; }
    }

    /// <summary>
    /// 插件自己封装的 PLM 调用。URL/用户/Token 来自 TS 会话，不走 TS 的 WebPlmMiddle 业务方法。
    /// </summary>
    internal sealed class PlmApiClient
    {
        public const string CadOtype = "ty.inteplm.cad.CTyCADDoc";
        public const string SubFolderOtype = "ty.inteplm.folder.CTySubFolder";
        public const string CabinetOtype = "ty.inteplm.folder.CTyCabinet";
        public const string ProductContainer = "ty.inteplm.product.CTyPDMLinkProduct";
        public const string ProjectContainer = "ty.inteplm.project.CTyProject";
        public const string ReadRight = "读取";
        public const string ModifyRight = "修改";

        private readonly TsSession _session;

        public PlmApiClient(TsSession session)
        {
            _session = session ?? throw new ArgumentNullException("session");
        }

        public List<PlmCadDoc> GetCadDocsByOids(List<string> oids, Action<string, int, int> progress)
        {
            var result = new List<PlmCadDoc>();
            if (oids == null || oids.Count == 0)
            {
                return result;
            }

            var unique = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string oid in oids)
            {
                if (string.IsNullOrWhiteSpace(oid) || !seen.Add(oid.Trim()))
                {
                    continue;
                }

                unique.Add(oid.Trim());
            }

            const int batchSize = 40;
            int total = unique.Count;
            for (int offset = 0; offset < total; offset += batchSize)
            {
                int count = Math.Min(batchSize, total - offset);
                Report(progress, "正在查询 CAD 文档 " + (offset + count) + "/" + total + "…",
                    offset + count, total);

                var docOidList = new List<Dictionary<string, object>>();
                for (int i = 0; i < count; i++)
                {
                    docOidList.Add(new Dictionary<string, object>
                    {
                        { "docOid", unique[offset + i] },
                        { "docOType", "" }
                    });
                }

                var input = new Dictionary<string, object>
                {
                    { "userOid", _session.UserOid ?? string.Empty },
                    { "docOIDList", docOidList }
                };
                Dictionary<string, object> response = PostEpm("getCADDocListByOIDS", input);
                IList list = JsonUtil.GetList(response, "data");
                if (list == null)
                {
                    continue;
                }

                foreach (object item in list)
                {
                    PlmCadDoc doc = ParseCad(JsonUtil.AsObject(item));
                    if (doc != null && !string.IsNullOrEmpty(doc.DocOid))
                    {
                        result.Add(doc);
                    }
                }
            }

            int withPath = 0;
            foreach (PlmCadDoc d in result)
            {
                if (!string.IsNullOrWhiteSpace(d.FolderPath))
                {
                    withPath++;
                }
            }

            AddinLog.Info("getCADDocListByOIDS 请求=" + unique.Count
                + " 返回=" + result.Count
                + " 含folderPath=" + withPath
                + (result.Count > 0 ? " 样例=" + result[0].FolderPath : ""));
            return result;
        }

        public string GetFolderPath(string folderOid, string folderOtype)
        {
            if (string.IsNullOrWhiteSpace(folderOid))
            {
                return string.Empty;
            }

            var input = new Dictionary<string, object>
            {
                { "folderOid", folderOid },
                { "folderOtype", string.IsNullOrWhiteSpace(folderOtype) ? SubFolderOtype : folderOtype },
                { "userOid", _session.UserOid }
            };
            Dictionary<string, object> response = PostEpm("getFolderPathByFolderId", input);
            return JsonUtil.GetString(response, "data");
        }

        public List<PlmAccess> CheckAccess(List<CadPermissionRow> targets)
        {
            var objects = new List<Dictionary<string, object>>();
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CadPermissionRow row in targets)
            {
                AddAccessTarget(objects, names, seen, row.DocOid, row.DocOtype);
                AddAccessTarget(objects, names, seen, row.FolderOid, row.FolderOtype);
            }

            var result = new List<PlmAccess>();
            if (objects.Count == 0)
            {
                return result;
            }

            var body = new Dictionary<string, object>
            {
                { "objects", objects },
                { "permissionNames", names.ToArray() }
            };
            string url = (_session.OriginUrl ?? string.Empty).TrimEnd('/')
                + "/rest/v1/webTsRemote/access/checkAccessByObjectId";
            Dictionary<string, object> response = PostRaw(url, body);
            IList list = JsonUtil.GetList(response, "data");
            if (list == null)
            {
                return result;
            }

            foreach (object item in list)
            {
                Dictionary<string, object> map = JsonUtil.AsObject(item);
                if (map == null)
                {
                    continue;
                }

                string authorized = JsonUtil.GetString(map, "isAuthorized", "authorized");
                bool? flag = null;
                if (!string.IsNullOrEmpty(authorized))
                {
                    flag = !authorized.Equals("NO", StringComparison.OrdinalIgnoreCase)
                        && !authorized.Equals("false", StringComparison.OrdinalIgnoreCase)
                        && authorized != "0";
                }

                result.Add(new PlmAccess
                {
                    ObjectOid = JsonUtil.GetString(map, "objectoid", "objectOid", "oid"),
                    Missing = JsonUtil.GetString(map, "access"),
                    Authorized = flag
                });
            }

            AddinLog.Info("checkAccessByObjectId 对象=" + objects.Count + " 返回=" + result.Count);
            return result;
        }

        public void Fill(List<CadPermissionRow> rows, Action<string, int, int> progress)
        {
            var oids = new List<string>();
            foreach (CadPermissionRow row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.DocOid))
                {
                    oids.Add(row.DocOid);
                }
            }

            Report(progress, "正在查询 CAD 文档（共 " + oids.Count + " 个）…", 0, 0);
            List<PlmCadDoc> docs = GetCadDocsByOids(oids, progress);
            var byOid = new Dictionary<string, PlmCadDoc>(StringComparer.OrdinalIgnoreCase);
            foreach (PlmCadDoc doc in docs)
            {
                if (!string.IsNullOrEmpty(doc.DocOid) && !byOid.ContainsKey(doc.DocOid))
                {
                    byOid[doc.DocOid] = doc;
                }
            }

            var pathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (CadPermissionRow row in rows)
            {
                index++;
                Report(progress, "正在整理文档信息 " + index + "/" + rows.Count + "…", index, rows.Count);
                PlmCadDoc doc;
                if (string.IsNullOrWhiteSpace(row.DocOid)
                    || !byOid.TryGetValue(row.DocOid, out doc))
                {
                    row.DocRead = row.DocModify = row.FolderRead = row.FolderModify = "未知";
                    continue;
                }

                ApplyCad(row, doc, pathCache);
                if (index == 1)
                {
                    AddinLog.Info("CAD 回填 代号=" + row.Number
                        + " 名称=" + row.Name
                        + " 路径=" + row.FolderPath
                        + " folderOid=" + row.FolderOid);
                }
            }

            Report(progress, "正在校验读取/修改权限…", 0, 0);
            List<PlmAccess> accesses = CheckAccess(rows);
            var accessMap = new Dictionary<string, PlmAccess>(StringComparer.OrdinalIgnoreCase);
            foreach (PlmAccess access in accesses)
            {
                if (!string.IsNullOrEmpty(access.ObjectOid))
                {
                    accessMap[access.ObjectOid] = access;
                }
            }

            foreach (CadPermissionRow row in rows)
            {
                ApplyAccess(row, accessMap);
            }

            Report(progress, "数据加载完成", 1, 1);
        }

        private static void Report(Action<string, int, int> progress, string message, int current, int maximum)
        {
            if (progress != null)
            {
                progress(message, current, maximum);
            }
        }

        private void ApplyCad(CadPermissionRow row, PlmCadDoc doc, Dictionary<string, string> pathCache)
        {
            row.DocOid = doc.DocOid;
            row.DocOtype = string.IsNullOrEmpty(doc.DocOtype) ? CadOtype : doc.DocOtype;
            row.FolderOid = doc.FolderOid;
            row.FolderOtype = ResolveFolderOtype(doc);
            row.DomainName = MapDomain(doc.ContainerType, doc.ContainerName);
            row.Number = FirstNonEmpty(doc.Number, row.Number);
            row.Name = FirstNonEmpty(doc.Name, doc.FileName, row.Name);

            // CadDocVO.folderPath 已是 CAD 文档文件夹全路径，直接展示，不再拼「产品库/项目库」。
            if (!string.IsNullOrWhiteSpace(doc.FolderPath))
            {
                row.FolderPath = doc.FolderPath.Trim();
                return;
            }

            string path = string.Empty;
            if (!string.IsNullOrWhiteSpace(doc.FolderOid))
            {
                string key = doc.FolderOid + "|" + row.FolderOtype;
                if (!pathCache.TryGetValue(key, out path))
                {
                    try
                    {
                        AddinLog.Info("CAD 无 folderPath，备用 getFolderPathByFolderId " + doc.FolderOid);
                        path = GetFolderPath(doc.FolderOid, row.FolderOtype);
                    }
                    catch (Exception ex)
                    {
                        AddinLog.Info("getFolderPathByFolderId 失败 " + doc.FolderOid + ": " + ex.Message);
                        path = string.Empty;
                    }

                    pathCache[key] = path;
                }
            }

            row.FolderPath = PrefixDomain(path, row.DomainName);
        }

        private static void ApplyAccess(CadPermissionRow row, Dictionary<string, PlmAccess> map)
        {
            row.DocRead = FormatAccess(map, row.DocOid, ReadRight);
            row.DocModify = FormatAccess(map, row.DocOid, ModifyRight);
            row.FolderRead = FormatAccess(map, row.FolderOid, ReadRight);
            row.FolderModify = FormatAccess(map, row.FolderOid, ModifyRight);
        }

        private static string FormatAccess(Dictionary<string, PlmAccess> map, string oid, string right)
        {
            if (string.IsNullOrEmpty(oid))
            {
                return "未知";
            }

            PlmAccess access;
            if (!map.TryGetValue(oid, out access))
            {
                return "未知";
            }

            if (!string.IsNullOrEmpty(access.Missing) && access.Missing.IndexOf(right, StringComparison.Ordinal) >= 0)
            {
                return "无";
            }

            return "有";
        }

        private static string ResolveFolderOtype(PlmCadDoc doc)
        {
            if (!string.IsNullOrWhiteSpace(doc.FolderOtype))
            {
                return doc.FolderOtype;
            }

            if (!string.IsNullOrEmpty(doc.FolderOid)
                && string.Equals(doc.FolderOid, doc.CabinetOid, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(doc.CabinetOtype))
            {
                return doc.CabinetOtype;
            }

            return SubFolderOtype;
        }

        private static string MapDomain(string containerType, string containerName)
        {
            string blob = (containerType ?? "") + " " + (containerName ?? "");
            if (blob.IndexOf("CTyProject", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("项目库", StringComparison.Ordinal) >= 0
                || blob.IndexOf("项目", StringComparison.Ordinal) >= 0)
            {
                return "项目库";
            }

            if (blob.IndexOf("CTyPDMLinkProduct", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("产品库", StringComparison.Ordinal) >= 0
                || blob.IndexOf("产品", StringComparison.Ordinal) >= 0)
            {
                return "产品库";
            }

            return string.Empty;
        }

        private static string PrefixDomain(string path, string domain)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return domain ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(domain))
            {
                return path;
            }

            if (path.IndexOf("产品库", StringComparison.Ordinal) >= 0
                || path.IndexOf("项目库", StringComparison.Ordinal) >= 0
                || path.StartsWith(domain, StringComparison.Ordinal))
            {
                return path;
            }

            return domain + "\\" + path.TrimStart('\\', '/');
        }

        private static PlmCadDoc ParseCad(Dictionary<string, object> map)
        {
            if (map == null)
            {
                return null;
            }

            string number = JsonUtil.GetString(map, "code");
            string folderOid = JsonUtil.GetString(map, "folderId", "folderOid", "subfolderOid", "locationID", "locationId");
            return new PlmCadDoc
            {
                Number = number,
                Name = JsonUtil.GetString(map, "name", "docName", "dlgname"),
                FileName = JsonUtil.GetString(map, "fileName", "filename", "dlgname"),
                DocOid = JsonUtil.GetString(map, "docId", "oid", "id"),
                DocOtype = FirstNonEmpty(JsonUtil.GetString(map, "otype", "docOtype"), CadOtype),
                FolderOid = folderOid,
                FolderOtype = JsonUtil.GetString(map, "subfolderOtype", "folderOtype"),
                FolderPath = JsonUtil.GetString(map, "folderPath"),
                ContainerType = JsonUtil.GetString(map, "containerType", "containerOtype"),
                ContainerName = JsonUtil.GetString(map, "containerName"),
                CabinetOid = JsonUtil.GetString(map, "cabinetOid"),
                CabinetOtype = JsonUtil.GetString(map, "cabinetOtype")
            };
        }

        private static void AddAccessTarget(
            List<Dictionary<string, object>> objects,
            List<string> names,
            HashSet<string> seen,
            string oid,
            string otype)
        {
            if (string.IsNullOrWhiteSpace(oid) || !seen.Add(oid))
            {
                return;
            }

            object oidValue = oid;
            long number;
            if (long.TryParse(oid, out number))
            {
                oidValue = number;
            }

            objects.Add(new Dictionary<string, object>
            {
                { "oid", oidValue },
                { "otype", string.IsNullOrWhiteSpace(otype) ? CadOtype : otype }
            });
            names.Add(ReadRight + "_" + ModifyRight);
        }

        private Dictionary<string, object> PostEpm(string apiName, Dictionary<string, object> input)
        {
            var envelope = new Dictionary<string, object>
            {
                { "orderID", 111 },
                { "clientID", 111 },
                { "userID", _session.UserOid },
                { "input", input }
            };
            string url = _session.EpmBaseUrl.TrimEnd('/') + "/" + apiName;
            return PostRaw(url, envelope);
        }

        private Dictionary<string, object> PostRaw(string url, Dictionary<string, object> body)
        {
            string json = JsonUtil.Serialize(body);
            string text = PlmHttpClient.PostJson(url, json, _session);
            Dictionary<string, object> map = JsonUtil.ParseObject(text);
            if (map == null)
            {
                throw new InvalidOperationException("后端返回不是 JSON 对象: " + url);
            }

            string result = JsonUtil.GetString(map, "result");
            bool success = JsonUtil.GetBool(map, "success")
                || result.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase);
            if (!success)
            {
                string error = ReadError(map);
                throw new InvalidOperationException(string.IsNullOrEmpty(error) ? (url + " 调用失败") : error);
            }

            return map;
        }

        private static string ReadError(Dictionary<string, object> map)
        {
            string message = JsonUtil.GetString(map, "message");
            object errors = null;
            foreach (KeyValuePair<string, object> pair in map)
            {
                if (string.Equals(pair.Key, "errors", StringComparison.OrdinalIgnoreCase))
                {
                    errors = pair.Value;
                    break;
                }
            }

            Dictionary<string, object> errObj = JsonUtil.AsObject(errors);
            if (errObj != null)
            {
                message = FirstNonEmpty(JsonUtil.GetString(errObj, "detail", "message"), message);
            }

            IList list = JsonUtil.AsList(errors);
            if (list != null && list.Count > 0)
            {
                Dictionary<string, object> first = JsonUtil.AsObject(list[0]);
                if (first != null)
                {
                    message = FirstNonEmpty(JsonUtil.GetString(first, "message", "detail"), message);
                }
            }

            return message;
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
