﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CacheManager.Core;
using VirtoCommerce.CatalogModule.Data.Converters;
using VirtoCommerce.CatalogModule.Data.Repositories;
using VirtoCommerce.Domain.Catalog.Services;
using VirtoCommerce.Domain.Commerce.Model;
using VirtoCommerce.Domain.Commerce.Services;
using VirtoCommerce.Platform.Core.Common;
using coreModel = VirtoCommerce.Domain.Catalog.Model;

namespace VirtoCommerce.CatalogModule.Data.Services
{
    public class ItemServiceImpl : CatalogServiceBase, IItemService
    {
        private readonly ICommerceService _commerceService;
        private readonly IOutlineService _outlineService;

        public ItemServiceImpl(Func<ICatalogRepository> catalogRepositoryFactory, ICommerceService commerceService, IOutlineService outlineService, ICacheManager<object> cacheManager)
            : base(catalogRepositoryFactory, cacheManager)
        {
            _commerceService = commerceService;
            _outlineService = outlineService;
        }

        #region IItemService Members

        public coreModel.CatalogProduct GetById(string itemId, coreModel.ItemResponseGroup respGroup, string catalogId = null)
        {
            var results = this.GetByIds(new[] { itemId }, respGroup, catalogId);
            return results.Any() ? results.First() : null;
        }

        public coreModel.CatalogProduct[] GetByIds(string[] itemIds, coreModel.ItemResponseGroup respGroup, string catalogId = null)
        {
            var result = new ConcurrentBag<coreModel.CatalogProduct>();
            Model.Item[] dataItems;
            using (var repository = base.CatalogRepositoryFactory())
            {
                dataItems = repository.GetItemByIds(itemIds, respGroup);
            }
            //Parallel conversation for better performance
            Parallel.ForEach(dataItems, (x) =>
            {
                result.Add(x.ToCoreModel(base.AllCachedCatalogs, base.AllCachedCategories));
            });


            // Fill outlines for products
            if (respGroup.HasFlag(coreModel.ItemResponseGroup.Outlines))
            {
                _outlineService.FillOutlinesForObjects(result, catalogId);
            }

            // Fill SEO info for products, variations and outline items
            if ((respGroup & coreModel.ItemResponseGroup.Seo) == coreModel.ItemResponseGroup.Seo)
            {
                var objectsWithSeo = new List<ISeoSupport>(result);

                var variations = result.Where(p => p.Variations != null)
                                       .SelectMany(p => p.Variations);
                objectsWithSeo.AddRange(variations);

                var outlineItems = result.Where(p => p.Outlines != null)
                                         .SelectMany(p => p.Outlines.SelectMany(o => o.Items));
                objectsWithSeo.AddRange(outlineItems);

                _commerceService.LoadSeoForObjects(objectsWithSeo.ToArray());
            }

            //Cleanup result model considered requested response group
            foreach (var product in result)
            {
                if (!respGroup.HasFlag(coreModel.ItemResponseGroup.ItemProperties))
                {
                    product.Properties = null;
                }             
            }

            return result.ToArray();
        }

        public void Create(coreModel.CatalogProduct[] items)
        {
            var pkMap = new PrimaryKeyResolvingMap();
            using (var repository = base.CatalogRepositoryFactory())
            {
                foreach (var item in items)
                {
                    var dbItem = item.ToDataModel(pkMap);
                    if (item.Variations != null)
                    {
                        foreach (var variation in item.Variations)
                        {
                            variation.MainProductId = dbItem.Id;
                            variation.CatalogId = dbItem.CatalogId;
                            var dbVariation = variation.ToDataModel(pkMap);
                            dbItem.Childrens.Add(dbVariation);
                        }
                    }
                    repository.Add(dbItem);
                }
                CommitChanges(repository);
                pkMap.ResolvePrimaryKeys();
            }

            //Update SEO 
            var itemsWithVariations = items.Concat(items.Where(x => x.Variations != null).SelectMany(x => x.Variations)).ToArray();
            _commerceService.UpsertSeoForObjects(itemsWithVariations);
        }

        public coreModel.CatalogProduct Create(coreModel.CatalogProduct item)
        {
            Create(new[] { item });

            var retVal = GetById(item.Id, coreModel.ItemResponseGroup.ItemLarge);
            return retVal;
        }

        public void Update(coreModel.CatalogProduct[] items)
        {
            var pkMap = new PrimaryKeyResolvingMap();
            var now = DateTime.UtcNow;
            using (var repository = base.CatalogRepositoryFactory())
            using (var changeTracker = base.GetChangeTracker(repository))
            {
                var dbItems = repository.GetItemByIds(items.Select(x => x.Id).ToArray(), coreModel.ItemResponseGroup.ItemLarge);
                foreach (var dbItem in dbItems)
                {
                    var item = items.FirstOrDefault(x => x.Id == dbItem.Id);
                    if (item != null)
                    {
                        changeTracker.Attach(dbItem);

                        item.Patch(dbItem, pkMap);
                        //Force set ModifiedDate property to mark a product changed. Special for  partial update cases when product table not have changes
                        dbItem.ModifiedDate = DateTime.UtcNow;
                    }
                }
                CommitChanges(repository);
                pkMap.ResolvePrimaryKeys();
            }

            //Update seo for products
            _commerceService.UpsertSeoForObjects(items);

        }

        public void Delete(string[] itemIds)
        {
            var items = GetByIds(itemIds, coreModel.ItemResponseGroup.Seo | coreModel.ItemResponseGroup.Variations);
            using (var repository = base.CatalogRepositoryFactory())
            {
                repository.RemoveItems(itemIds);
                CommitChanges(repository);
            }      
        }
        #endregion
    }
}
