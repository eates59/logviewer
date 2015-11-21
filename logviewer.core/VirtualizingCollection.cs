﻿// Created by: egr
// Created at: 10.11.2015
// © 2012-2015 Alexander Egorov

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace logviewer.core
{
    /// <summary>
    ///     Specialized list implementation that provides data virtualization. The collection is divided up into pages,
    ///     and pages are dynamically fetched from the IItemsProvider when required. Stale pages are removed after a
    ///     configurable period of time.
    ///     Intended for use with large collections on a network or disk resource that cannot be instantiated locally
    ///     due to memory consumption or fetch latency.
    /// </summary>
    /// <remarks>
    ///     The IList implmentation is not fully complete, but should be sufficient for use as read only collection
    ///     data bound to a suitable ItemsControl.
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    public sealed class VirtualizingCollection<T> : IList<T>, IList, INotifyCollectionChanged, INotifyPropertyChanged
    {
        #region Constructors

        public VirtualizingCollection(IItemsProvider<T> itemsProvider, int pageSize, int pageCacheTimeoutMilliseconds)
        {
            this.ItemsProvider = itemsProvider;
            this.PageSize = pageSize;
            this.PageCacheTimeoutMilliseconds = pageCacheTimeoutMilliseconds;
        }

        public VirtualizingCollection(IItemsProvider<T> itemsProvider, int pageSize)
        {
            this.ItemsProvider = itemsProvider;
            this.PageSize = pageSize;
        }

        public VirtualizingCollection(IItemsProvider<T> itemsProvider)
        {
            this.ItemsProvider = itemsProvider;
        }

        #endregion

        public IItemsProvider<T> ItemsProvider { get; }

        public int PageSize { get; } = 100;

        public long PageCacheTimeoutMilliseconds { get; } = 10000;

        private readonly TaskScheduler uiSyncContext = TaskScheduler.FromCurrentSynchronizationContext();


        public event NotifyCollectionChangedEventHandler CollectionChanged;

        private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            this.CollectionChanged?.Invoke(this, e);
        }

        private void FireCollectionReset()
        {
            var e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
            this.OnCollectionChanged(e);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            this.PropertyChanged?.Invoke(this, e);
        }

        private void FirePropertyChanged(string propertyName)
        {
            var e = new PropertyChangedEventArgs(propertyName);
            this.OnPropertyChanged(e);
        }

        private bool isLoading;

        public bool IsLoading
        {
            get
            {
                return this.isLoading;
            }
            set
            {
                this.isLoading = value;
                this.FirePropertyChanged(nameof(this.IsLoading));
            }
        }

        private void LoadPage(int pageIndex)
        {
            var task = Task<IList<T>>.Factory.StartNew(() => this.FetchPage(pageIndex));

            task.ContinueWith(delegate (Task<IList<T>> t)
            {
                this.PopulatePage(pageIndex, t.Result);
                this.FireCollectionReset();
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, this.uiSyncContext);
        }

        /// <summary>
        /// First visible collection element
        /// </summary>
        public int FirstVisible { get; set; }

        /// <summary>
        /// Last visible collection element
        /// </summary>
        public int LastVisible { get; set; }
        

        #region IList<T>, IList

        #region Count

        private int count;

        public int Count
        {
            get
            {
                return this.count;
            }
            private set
            {
                this.count = value;
                this.FireCollectionReset();
            }
        }

        #endregion

        #region Indexer

        /// <summary>
        ///     Gets the item at the specified index. This property will fetch
        ///     the corresponding page from the IItemsProvider if required.
        /// </summary>
        /// <value></value>
        public T this[int index]
        {
            get
            {
                // determine which page and offset within page
                var pageIndex = index / this.PageSize;
                var pageOffset = index % this.PageSize;

                // do not load invisible. Because WPF try to iterate all collection on Reset event
                if (index >= this.FirstVisible && index <= this.LastVisible)
                {
                    // request primary page
                    this.RequestPage(pageIndex);

                    this.CleanUpPages();
                }

                // defensive check in case of async load
                IList<T> result;
                if (!this.pages.TryGetValue(pageIndex, out result) || result == null)
                {
                    return default(T);
                }
                return pageOffset >= result.Count ? default(T) : result[pageOffset];
            }
            set { throw new NotSupportedException(); }
        }

        object IList.this[int index]
        {
            get { return this[index]; }
            set { throw new NotSupportedException(); }
        }

        #endregion

        public IEnumerator<T> GetEnumerator()
        {
            for (var i = 0; i < this.Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public void Add(T item)
        {
            throw new NotSupportedException();
        }

        int IList.Add(object value)
        {
            throw new NotSupportedException();
        }

        bool IList.Contains(object value)
        {
            return this.Contains((T) value);
        }

        public bool Contains(T item)
        {
            return false;
        }

        public void Clear()
        {
            this.count = 0;
            this.pages.Clear();
            this.pageTouchTimes.Clear();
        }

        int IList.IndexOf(object value)
        {
            return this.IndexOf((T) value);
        }

        public int IndexOf(T item)
        {
            return -1;
        }

        public void Insert(int index, T item)
        {
            throw new NotSupportedException();
        }

        void IList.Insert(int index, object value)
        {
            this.Insert(index, (T) value);
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        void IList.Remove(object value)
        {
            throw new NotSupportedException();
        }

        public bool Remove(T item)
        {
            throw new NotSupportedException();
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            throw new NotSupportedException();
        }

        void ICollection.CopyTo(Array array, int index)
        {
            throw new NotSupportedException();
        }

        public object SyncRoot => this;
        public bool IsSynchronized => false;
        public bool IsReadOnly => true;

        public bool IsFixedSize => false;

        #endregion

        #region Paging

        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        private Dictionary<int, IList<T>> pages = new Dictionary<int, IList<T>>();
        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        private Dictionary<int, DateTime> pageTouchTimes = new Dictionary<int, DateTime>();

        /// <summary>
        ///     Cleans up any stale pages that have not been accessed in the period dictated by PageCacheTimeoutMilliseconds.
        /// </summary>
        public void CleanUpPages()
        {
            // TODO: performance problem but in other hand you cannot modify collection (this.pageTouchTimes) while iterating
            var keys = new List<int>(this.pageTouchTimes.Keys);

            // Performance reason. Thanks dotTrace from JetBrains :)
            var now = DateTime.Now;
            foreach (var key in keys)
            {
                // page 0 is a special case, since WPF ItemsControl access the first item frequently
                DateTime lastUsed;
                if (key != 0 && this.pageTouchTimes.TryGetValue(key, out lastUsed) && (now - lastUsed).TotalMilliseconds > this.PageCacheTimeoutMilliseconds)
                {
                    this.pages.Remove(key);
                    this.pageTouchTimes.Remove(key);
                    Trace.WriteLine("Removed Page: " + key);
                }
            }
        }

        /// <summary>
        ///     Populates the page within the dictionary.
        /// </summary>
        /// <param name="pageIndex">Index of the page.</param>
        /// <param name="page">The page.</param>
        private void PopulatePage(int pageIndex, IList<T> page)
        {
            Trace.WriteLine("Page populated: " + pageIndex);
            if (this.pages.ContainsKey(pageIndex))
            {
                this.pages[pageIndex] = page;
            }
        }

        /// <summary>
        ///     Makes a request for the specified page, creating the necessary slots in the dictionary,
        ///     and updating the page touch time.
        /// </summary>
        private void RequestPage(int pageIndex)
        {
            if (!this.pages.ContainsKey(pageIndex))
            {
                this.pages.Add(pageIndex, null);
                this.pageTouchTimes.Add(pageIndex, DateTime.Now);
                Trace.WriteLine("Added page: " + pageIndex);
                this.LoadPage(pageIndex);
            }
            else
            {
                this.pageTouchTimes[pageIndex] = DateTime.Now;
            }
        }

        #endregion

        /// <summary>
        ///     Loads the count of items.
        /// </summary>
        public void LoadCount(int itemsCount)
        {
            this.Count = itemsCount;
        }

        private IList<T> FetchPage(int pageIndex)
        {
            return this.ItemsProvider.FetchRange(pageIndex * this.PageSize, this.PageSize);
        }
    }
}