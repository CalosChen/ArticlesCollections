using Abp;
using Abp.Dependency;
using Abp.Runtime;
using Abp.Runtime.Session;
using Abp.Threading;
using EP.Commons.Core.Configuration;
using EP.Commons.ServiceApi;
using EP.Commons.ServiceApi.UserCenter;
using EP.Commons.ServiceApi.UserCenter.Dto;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EP.DynamicForms.Helpers
{


    public class MongoHelper : IMongoHelper, ITransientDependency
    {
        #region Fields
        //数据项key
        private const string DataContextKey = "EP.DynamicForms.Helpers.MongoHelper";

        private const string TENANT_ID_KEY = "TenantId";

        private static IConfigurationRoot ConfigurationRoot => AppConfigurations.GetConfiguration(Commons.Core.Web.WebContentDirectoryFinder.CalculateContentRootFolder());

        private int? TenantId => GetCurrentItem(DataContextKey)?.Value ?? IocManager.Instance.Resolve<IAbpSession>()?.TenantId;

        private static readonly string MONGO_ADDRESS = ConfigurationRoot["ConnectionStrings:Mongodb"];

        private static readonly string databaseName = ConfigurationRoot["CustomConfig:Db:MongodbDBName"];
        private IUserCenterServiceApi userCenterServiceApi;
        protected IMongoClient _client;
        protected IMongoDatabase _database;
        #endregion


        #region Constructors

        public MongoHelper(IServiceApiFactory serviceApiFactory, IAbpSession abpSession)
        {
            string dbname = databaseName;
            _client = new MongoClient(MONGO_ADDRESS);
            _database = _client.GetDatabase(dbname);
            _dataContext = IocManager.Instance.Resolve<IAmbientDataContext>();
            userCenterServiceApi = serviceApiFactory.GetServiceApi<IUserCenterServiceApi>().Object;
            AbpSession = abpSession;
            //EnsureCollectionsCreated();
        }

        private List<TenantDto> Tenants()
        {
            return AsyncHelper.RunSync(() => userCenterServiceApi.GetAllTenants());
        }

        public void CreateCollectionsForTenant(TenantDto t)
        {
            string index = EnsureIndexCollectionCreated();
            string[] collections = new string[] { DynamicFormsConsts.FormEditorTreeCollectionName, DynamicFormsConsts.FormDataCollectionName };
            foreach (string name in collections)
            {
                string tenantDbName = GetCollectionName(name);
                IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(tenantDbName);
                if (collection == null)
                {
                    _database.CreateCollection(tenantDbName);
                }
                IMongoCollection<BsonDocument> ids = _database.GetCollection<BsonDocument>(index);
                if (!ids.Find(Builder.FilterEq("name", name) & Builder.FilterEq("TenantId", t.Id)).Any())
                {
                    ids.InsertOne(new BsonDocument(new BsonElement("id", 0), new BsonElement("name", name), new BsonElement("TenantId", t.Id)));
                }
            }
        }

        private string EnsureIndexCollectionCreated()
        {
            string index = "ids";
            IMongoCollection<BsonDocument> indexCollection = _database.GetCollection<BsonDocument>(index);
            if (indexCollection == null)
            {
                _database.CreateCollection(index);
            }
            return index;
        }

        public void EnsureCollectionsCreated()
        {
            Tenants().ForEach(t =>
            {
                CreateCollectionsForTenant(t);
            });
        }
        private string GetCollectionName(string collectionName)
        {
            return collectionName + AbpSession.TenantId ?? "";
        }

        public int IncrementId(string collectionName)
        {
            string command = @"{findAndModify:'ids',update:{$inc:{'id':1}},query:{'name':'" + collectionName + "','TenantId':" + AbpSession.TenantId ?? "" + "},new:true  }";
            BsonDocument identity = _database.RunCommand<BsonDocument>(command);
            return identity["value"]["id"].ToInt32();
        }


        #endregion

        #region Select
        #region Select Count
        /// <summary>
        /// Returns the count of all recors in the specified collection
        /// </summary>
        /// <param name="collectionName"></param>
        /// <returns></returns>
        public long? SelectCount(string collectionName, bool isMultitenancy = true)
        {
            try
            {
                BsonDocument defFilter = new BsonDocument(); ;
                IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(collectionName);
                return collection.CountDocuments(isMultitenancy ? Builder.FilterEq("tenantId", TenantId) : defFilter);
            }
            catch (Exception)
            {
                return null;
            }
        }
        /// <summary>
        /// Select count 
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public long SelectCount(string collectionName, FilterDefinition<BsonDocument> filter, bool isMultitenancy = true)
        {
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(collectionName);
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }

            return collection.CountDocuments(filter);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="field"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public long SelectCount(string collectionName, string field, string value, bool isMultitenancy = true)
        {
            FilterDefinition<BsonDocument> filter = Builder.FilterEq(field, value);
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(collectionName);
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }

            return collection.CountDocuments(filter);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="field"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public long SelectCount(string collectionName, string field, ObjectId id, bool isMultitenancy = true)
        {
            FilterDefinition<BsonDocument> filter = Builder.FilterEq(field, id);
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(collectionName);
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }

            return collection.CountDocuments(filter);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collectionName"></param>
        /// <param name="field"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public long SelectCount<T>(string collectionName, string field, T value, bool isMultitenancy = true)
        {
            FilterDefinition<BsonDocument> filter = Builder.FilterEq<T>(field, value);
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(collectionName);
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }

            return collection.CountDocuments(filter);
        }
        public long Count(string collectionName, FilterDefinition<BsonDocument> filter, bool isMultitenancy = true)
        {
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(collectionName);
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }

            return collection.CountDocuments(filter);
        }
        #endregion
        /// <summary>
        /// Returns a list of the given type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collectionName"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public List<T> Select<T>(string collectionName, FilterDefinition<BsonDocument> filter, bool isMultitenancy = true)
        {
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }

            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(collectionName);
            List<BsonDocument> result = collection.Find(filter).ToList();
            List<T> returnList = new List<T>();
            foreach (BsonDocument l in result)
            {
                returnList.Add(BsonSerializer.Deserialize<T>(l));
            }
            return returnList;
        }
        /// <summary>
        /// Select a single record of the given type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collectionName"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public T SelectOne<T>(string collectionName, FilterDefinition<BsonDocument> filter, bool isMultitenancy = true)
        {
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }

            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(collectionName);
            List<BsonDocument> result = collection.Find(filter).ToList();
            if (result.Count > 1)
            {
                throw new Exception("To many results");
            }
            BsonDocument firstEl = result.ElementAt(0);
            return BsonSerializer.Deserialize<T>(firstEl);
        }
        public BsonDocument SelectOne(string collectionName, FilterDefinition<BsonDocument> filter, bool isMultitenancy = true)
        {
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }

            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(collectionName);
            IFindFluent<BsonDocument, BsonDocument> set = collection.Find(filter);
            return set.FirstOrDefault();
        }
        public async Task<List<BsonDocument>> SelectManyAsync(string collectionName, FilterDefinition<BsonDocument> filter, bool queryAll = false, int pageIndex = 1, int pageSize = int.MaxValue, bool isMultitenancy = true)
        {
            List<BsonDocument> ret = new List<BsonDocument>();
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }

            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(collectionName);
            IFindFluent<BsonDocument, BsonDocument> set = collection.Find(filter);
            if (!queryAll)
            {
                set = set.Skip((pageIndex - 1) * pageSize).Limit(pageSize);
            }

            await set.ForEachAsync(doc => { ret.Add(doc); });
            return ret;
        }
        #endregion

        #region Insert
        /// <summary>
        /// Insert a BsonDocument 
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="doc"></param>
        /// <returns></returns>
        public void Insert(string collectionName, BsonDocument doc, bool isMultitenancy = true)
        {
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(GetCollectionName(collectionName));
            if (isMultitenancy)
            {
                AttachTenantIdToData(ref doc);
            }

            collection.InsertOne(doc);
        }
        /// <summary>
        /// Insert an object of any type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collectionName"></param>
        /// <param name="doc"></param>
        /// <returns></returns>
        public void Insert<T>(string collectionName, T doc, bool isMultitenancy = true)
        {

            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(GetCollectionName(collectionName));
            BsonDocument bson = doc.ToBsonDocument();
            if (isMultitenancy)
            {
                AttachTenantIdToData(ref bson);
            }

            collection.InsertOne(bson);

        }
        /// <summary>
        /// Insert multiple objects into a collection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collectionName"></param>
        /// <param name="documents"></param>
        /// <returns></returns>
        public void InsertMany<T>(string collectionName, IEnumerable<T> documents, bool isMultitenancy = true)
        {

            List<BsonDocument> docs = new List<BsonDocument>();
            for (int i = 0; i < documents.Count(); i++)
            {
                BsonDocument doc = documents.ElementAt(i).ToBsonDocument();
                if (isMultitenancy)
                {
                    AttachTenantIdToData(ref doc);
                }

                docs[i] = doc;
            }
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(GetCollectionName(collectionName));
            collection.InsertMany(docs);
        }
        #endregion

        #region Update
        /// <summary>
        /// Update an object
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="filter"></param>
        /// <param name="update"></param>
        /// <returns></returns>
        public void UpdateOne(string collectionName, FilterDefinition<BsonDocument> filter, UpdateDefinition<BsonDocument> update, bool isMultitenancy = true)
        {
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(GetCollectionName(collectionName));
            collection.UpdateOne(filter, update);
        }

        /// <summary>
        /// Update an object
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="fieldName">The field name to identify the object to be updated</param>
        /// <param name="value">The value to the identifier field</param>
        /// <param name="update"></param>
        /// <returns></returns>
        public void UpdateOne(string collectionName, string fieldName, string value, UpdateDefinition<BsonDocument> update, bool isMultitenancy = true)
        {
            FilterDefinition<BsonDocument> filter = Builder.FilterEq(fieldName, value);
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(GetCollectionName(collectionName));
            collection.UpdateOne(filter, update);
        }

        /// <summary>
        /// Update an Array inside an object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collectionName"></param>
        /// <param name="arrayField"></param>
        /// <param name="list"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public void UpdateArray<T>(string collectionName, string arrayField, List<T> list, FilterDefinition<BsonDocument> filter, bool isMultitenancy = true)
        {
            if (isMultitenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(GetCollectionName(collectionName));
            UpdateDefinition<BsonDocument> update = Builders<BsonDocument>.Update.PushEach(arrayField, list);
            collection.FindOneAndUpdate<BsonDocument>(filter, update);
        }
        #endregion

        #region Delete
        /// <summary>
        /// Remove objects by condition
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public void Delete(string collectionName, FilterDefinition<BsonDocument> filter, bool isMultiTenancy = true)
        {
            if (isMultiTenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }
            IMongoCollection<BsonDocument> collection = _database.GetCollection<BsonDocument>(GetCollectionName(collectionName));
            collection.DeleteMany(filter);
        }

        /// <summary>
        /// Remove an object from a collection
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="fieldName"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public void Delete(string collectionName, string fieldName, string value, bool isMultiTenancy = true)
        {
            FilterDefinition<BsonDocument> filter = Builder.FilterEq(fieldName, value);
            if (isMultiTenancy)
            {
                ApplyMultiTenantFilter(ref filter);
            }
            Delete(collectionName, filter);
        }
        #endregion





        #region 多租户过滤器
        private void ApplyMultiTenantFilter(ref FilterDefinition<BsonDocument> filter)
        {
            filter = filter & Builder.FilterEq(TENANT_ID_KEY, TenantId);
        }

        private void AttachTenantIdToData(ref BsonDocument bsonElements)
        {
            bsonElements["tenantId"] = TenantId;
        }
        //get tenant dbname, transfer db name

        #endregion

        public IDisposable Use(int? tenantId)
        {
            ScopeItem item = new ScopeItem(tenantId, GetCurrentItem(DataContextKey));

            string key = item.Id;
            if (!ConcurrentItems.TryAdd(key, item))
            {
                throw new AbpException("Can not add item! ConcurrentItems.TryAdd returns false!");
            }

            _dataContext.SetData(DataContextKey, key);

            return new DisposeAction(() =>
            {
                ConcurrentItems.TryRemove(key, out item);
                if (item == null)
                {
                    _dataContext.SetData(DataContextKey, null);
                    return;
                }
                _dataContext.SetData(DataContextKey, item.Outer?.Id);
            });
        }

        private static ConcurrentDictionary<string, ScopeItem> ConcurrentItems { get; set; } = new ConcurrentDictionary<string, ScopeItem>();
        public IAbpSession AbpSession { get; }

        private IAmbientDataContext _dataContext;

        private ScopeItem GetCurrentItem(string contextKey)
        {
            string objKey = _dataContext.GetData(contextKey) as string;
            return objKey != null ? ConcurrentItems.GetValueOrDefault(objKey) : null;
        }

        private class ScopeItem
        {
            public string Id { get; }

            public ScopeItem Outer { get; }

            public dynamic Value { get; }

            public ScopeItem(dynamic value, ScopeItem outer = null)
            {
                Id = Guid.NewGuid().ToString();

                Value = value;
                Outer = outer;
            }
        }
    }






}
