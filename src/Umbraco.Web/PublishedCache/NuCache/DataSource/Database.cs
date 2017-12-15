﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using NPoco;
using Umbraco.Core;
using Umbraco.Core.Logging;
using Umbraco.Core.Models.Rdbms;
using Umbraco.Core.Persistence;
using Umbraco.Core.Scoping;
using Umbraco.Core.Serialization;
using Umbraco.Web.Composing;
using static Umbraco.Core.Persistence.NPocoSqlExtensions.Statics;

namespace Umbraco.Web.PublishedCache.NuCache.DataSource
{
    // fixme - use SqlTemplate for these queries else it's going to be horribly slow!

    // provides efficient database access for NuCache
    internal class Database
    {
        // we want arrays, we want them all loaded, not an enumerable

        private Sql<ISqlContext> ContentSourcesSelect(IScope scope, Func<Sql<ISqlContext>, Sql<ISqlContext>> joins = null)
        {
            var sql = scope.SqlContext.Sql()

                .Select<NodeDto>(x => Alias(x.NodeId, "Id"), x => Alias(x.UniqueId, "Uid"),
                    x => Alias(x.Level, "Level"), x => Alias(x.Path, "Path"), x => Alias(x.SortOrder, "SortOrder"), x => Alias(x.ParentId, "ParentId"),
                    x => Alias(x.CreateDate, "CreateDate"), x => Alias(x.UserId, "CreatorId"))
                .AndSelect<ContentDto>(x => Alias(x.ContentTypeId, "ContentTypeId"))
                .AndSelect<DocumentDto>(x => Alias(x.Published, "Published"), x => Alias(x.Edited, "Edited"))

                .AndSelect<ContentVersionDto>(x => Alias(x.Id, "VersionId"), x => Alias(x.Text, "EditName"), x => Alias(x.VersionDate, "EditVersionDate"), x => Alias(x.UserId, "EditWriterId"))
                .AndSelect<DocumentVersionDto>(x => Alias(x.TemplateId, "EditTemplateId"))

                .AndSelect<ContentVersionDto>("pcver", x => Alias(x.Id, "PublishedVersionId"), x => Alias(x.Text, "PubName"), x => Alias(x.VersionDate, "PubVersionDate"), x => Alias(x.UserId, "PubWriterId"))
                .AndSelect<DocumentVersionDto>("pdver", x => Alias(x.TemplateId, "PubTemplateId"))

                .AndSelect<ContentNuDto>("nuEdit", x => Alias(x.Data, "EditData"))
                .AndSelect<ContentNuDto>("nuPub", x => Alias(x.Data, "PubData"))

                .From<NodeDto>();

            if (joins != null)
                sql = joins(sql);

            sql = sql
                .InnerJoin<ContentDto>().On<NodeDto, ContentDto>((left, right) => left.NodeId == right.NodeId)
                .InnerJoin<DocumentDto>().On<NodeDto, DocumentDto>((left, right) => left.NodeId == right.NodeId)

                .InnerJoin<ContentVersionDto>().On<NodeDto, ContentVersionDto>((left, right) => left.NodeId == right.NodeId && right.Current)
                .InnerJoin<DocumentVersionDto>().On<ContentVersionDto, DocumentVersionDto>((left, right) => left.Id == right.Id)

                .LeftJoin<ContentVersionDto>(j =>
                    j.InnerJoin<DocumentVersionDto>("pdver").On<ContentVersionDto, DocumentVersionDto>((left, right) => left.Id == right.Id && right.Published, "pcver", "pdver"), "pcver")
                .On<NodeDto, ContentVersionDto>((left, right) => left.NodeId == right.NodeId, aliasRight: "pcver")

                .LeftJoin<ContentNuDto>("nuEdit").On<NodeDto, ContentNuDto>((left, right) => left.NodeId == right.NodeId && !right.Published, aliasRight: "nuEdit")
                .LeftJoin<ContentNuDto>("nuPub").On<NodeDto, ContentNuDto>((left, right) => left.NodeId == right.NodeId && right.Published, aliasRight: "nuPub");

            return sql;
        }

        public ContentNodeKit GetContentSource(IScope scope, int id)
        {
            var sql = ContentSourcesSelect(scope)
                .Where<NodeDto>(x => x.NodeObjectType == Constants.ObjectTypes.Document && x.NodeId == id)
                .OrderBy<NodeDto>(x => x.Level, x => x.SortOrder);

            var dto = scope.Database.Fetch<ContentSourceDto>(sql).FirstOrDefault();
            return dto == null ? new ContentNodeKit() : CreateContentNodeKit(dto);
        }

        public IEnumerable<ContentNodeKit> GetAllContentSources(IScope scope)
        {
            var sql = ContentSourcesSelect(scope)
                .Where<NodeDto>(x => x.NodeObjectType == Constants.ObjectTypes.Document)
                .OrderBy<NodeDto>(x => x.Level, x => x.SortOrder);

            return scope.Database.Query<ContentSourceDto>(sql).Select(CreateContentNodeKit);
        }

        public IEnumerable<ContentNodeKit> GetBranchContentSources(IScope scope, int id)
        {
            var syntax = scope.SqlContext.SqlSyntax;
            var sql = ContentSourcesSelect(scope, s => s

                    .InnerJoin<NodeDto>("x").On<NodeDto, NodeDto>((left, right) => left.NodeId == right.NodeId || SqlText<bool>(left.Path, right.Path, (lp, rp) => $"({lp} LIKE {syntax.GetConcat(rp, "',%'")})"), aliasRight: "x"))

                .Where<NodeDto>(x => x.NodeObjectType == Constants.ObjectTypes.Document)
                .Where<NodeDto>(x => x.NodeId == id, "x")
                .OrderBy<NodeDto>(x => x.Level, x => x.SortOrder);

            return scope.Database.Query<ContentSourceDto>(sql).Select(CreateContentNodeKit);
        }

        public IEnumerable<ContentNodeKit> GetTypeContentSources(IScope scope, IEnumerable<int> ids)
        {
            var sql = ContentSourcesSelect(scope)
                .Where<NodeDto>(x => x.NodeObjectType == Constants.ObjectTypes.Document)
                .WhereIn<ContentDto>(x => x.ContentTypeId, ids)
                .OrderBy<NodeDto>(x => x.Level, x => x.SortOrder);

            return scope.Database.Query<ContentSourceDto>(sql).Select(CreateContentNodeKit);
        }

        private Sql<ISqlContext> MediaSourcesSelect(IScope scope, Func<Sql<ISqlContext>, Sql<ISqlContext>> joins = null)
        {
            var sql = scope.SqlContext.Sql()

                .Select<NodeDto>(x => Alias(x.NodeId, "Id"), x => Alias(x.UniqueId, "Uid"),
                    x => Alias(x.Level, "Level"), x => Alias(x.Path, "Path"), x => Alias(x.SortOrder, "SortOrder"), x => Alias(x.ParentId, "ParentId"),
                    x => Alias(x.CreateDate, "CreateDate"), x => Alias(x.UserId, "CreatorId"))
                .AndSelect<ContentDto>(x => Alias(x.ContentTypeId, "ContentTypeId"))

                .AndSelect<ContentVersionDto>(x => Alias(x.Id, "VersionId"), x => Alias(x.Text, "EditName"), x => Alias(x.VersionDate, "EditVersionDate"), x => Alias(x.UserId, "EditWriterId"))

                .AndSelect<ContentNuDto>("nuEdit", x => Alias(x.Data, "EditData"))

                .From<NodeDto>();

            if (joins != null)
                sql = joins(sql);

            sql = sql
                .InnerJoin<ContentDto>().On<NodeDto, ContentDto>((left, right) => left.NodeId == right.NodeId)

                .InnerJoin<ContentVersionDto>().On<NodeDto, ContentVersionDto>((left, right) => left.NodeId == right.NodeId && right.Current)

                .LeftJoin<ContentNuDto>("nuEdit").On<NodeDto, ContentNuDto>((left, right) => left.NodeId == right.NodeId && !right.Published, aliasRight: "nuEdit");

            return sql;
        }

        public ContentNodeKit GetMediaSource(IScope scope, int id)
        {
            var sql = MediaSourcesSelect(scope)
                .Where<NodeDto>(x => x.NodeObjectType == Constants.ObjectTypes.Media && x.NodeId == id)
                .OrderBy<NodeDto>(x => x.Level, x => x.SortOrder);

            var dto = scope.Database.Fetch<ContentSourceDto>(sql).FirstOrDefault();
            return dto == null ? new ContentNodeKit() : CreateContentNodeKit(dto);
        }

        public IEnumerable<ContentNodeKit> GetAllMediaSources(IScope scope)
        {
            var sql = MediaSourcesSelect(scope)
                .Where<NodeDto>(x => x.NodeObjectType == Constants.ObjectTypes.Media)
                .OrderBy<NodeDto>(x => x.Level, x => x.SortOrder);

            return scope.Database.Query<ContentSourceDto>(sql).Select(CreateMediaNodeKit);
        }

        public IEnumerable<ContentNodeKit> GetBranchMediaSources(IScope scope, int id)
        {
            var syntax = scope.SqlContext.SqlSyntax;
            var sql = MediaSourcesSelect(scope, s => s

                    .InnerJoin<NodeDto>("x").On<NodeDto, NodeDto>((left, right) => left.NodeId == right.NodeId || SqlText<bool>(left.Path, right.Path, (lp, rp) => $"({lp} LIKE {syntax.GetConcat(rp, "',%'")})"), aliasRight: "x"))

                .Where<NodeDto>(x => x.NodeObjectType == Constants.ObjectTypes.Media)
                .Where<NodeDto>(x => x.NodeId == id, "x")
                .OrderBy<NodeDto>(x => x.Level, x => x.SortOrder);

            return scope.Database.Query<ContentSourceDto>(sql).Select(CreateMediaNodeKit);
        }

        public IEnumerable<ContentNodeKit> GetTypeMediaSources(IScope scope, IEnumerable<int> ids)
        {
            var sql = MediaSourcesSelect(scope)
                    .Where<NodeDto>(x => x.NodeObjectType == Constants.ObjectTypes.Media)
                    .WhereIn<ContentDto>(x => x.ContentTypeId, ids)
                    .OrderBy<NodeDto>(x => x.Level, x => x.SortOrder);

            return scope.Database.Query<ContentSourceDto>(sql).Select(CreateMediaNodeKit);
        }

        private static ContentNodeKit CreateContentNodeKit(ContentSourceDto dto)
        {
            ContentData d = null;
            ContentData p = null;

            if (dto.EditData == null)
            {
                if (Debugger.IsAttached)
                    throw new Exception("Missing cmsContentNu edited content for node " + dto.Id + ", consider rebuilding.");
                Current.Logger.Warn<Database>("Missing cmsContentNu edited content for node " + dto.Id + ", consider rebuilding.");
            }
            else
            {
                d = new ContentData
                {
                    Name = dto.EditName,
                    Published = false,
                    TemplateId = dto.EditTemplateId,
                    VersionId = dto.VersionId,
                    VersionDate = dto.EditVersionDate,
                    WriterId = dto.EditWriterId,
                    Properties = DeserializeData(dto.EditData)
                };
            }

            if (dto.Published)
            {
                if (dto.PubData == null)
                {
                    if (Debugger.IsAttached)
                        throw new Exception("Missing cmsContentNu published content for node " + dto.Id + ", consider rebuilding.");
                    Current.Logger.Warn<Database>("Missing cmsContentNu published content for node " + dto.Id + ", consider rebuilding.");
                }
                else
                {
                    p = new ContentData
                    {
                        Name = dto.PubName,
                        Published = true,
                        TemplateId = dto.PubTemplateId,
                        VersionId = dto.VersionId,
                        VersionDate = dto.PubVersionDate,
                        WriterId = dto.PubWriterId,
                        Properties = DeserializeData(dto.PubData)
                    };
                }
            }

            var n = new ContentNode(dto.Id, dto.Uid,
                dto.Level, dto.Path, dto.SortOrder, dto.ParentId, dto.CreateDate, dto.CreatorId);

            var s = new ContentNodeKit
            {
                Node = n,
                ContentTypeId = dto.ContentTypeId,
                DraftData = d,
                PublishedData = p
            };

            return s;
        }

        private static ContentNodeKit CreateMediaNodeKit(ContentSourceDto dto)
        {
            if (dto.EditData == null)
                throw new Exception("No data for media " + dto.Id);

            var p = new ContentData
            {
                Name = dto.EditName,
                Published = true,
                TemplateId = -1,
                VersionId = dto.VersionId,
                VersionDate = dto.EditVersionDate,
                WriterId = dto.CreatorId, // what-else?
                Properties = DeserializeData(dto.EditData)
            };

            var n = new ContentNode(dto.Id, dto.Uid,
                dto.Level, dto.Path, dto.SortOrder, dto.ParentId, dto.CreateDate, dto.CreatorId);

            var s = new ContentNodeKit
            {
                Node = n,
                ContentTypeId = dto.ContentTypeId,
                PublishedData = p
            };

            return s;
        }

        private static Dictionary<string, PropertyData[]> DeserializeData(string data)
        {
            // by default JsonConvert will deserialize our numeric values as Int64
            // which is bad, because they were Int32 in the database - take care

            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new ForceInt32Converter() }
            };

            return JsonConvert.DeserializeObject<Dictionary<string, PropertyData[]>>(data, settings);
        }
    }
}