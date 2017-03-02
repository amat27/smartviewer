﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogFlow.DataModel
{
    public class FilteredView<T> : IFilteredView<T> where T : DataItemBase
    {
        #region Find, Count, Filter, Tag features.

        /// <summary>
        /// Tag or replace the color's filter.
        /// </summary>
        /// <param name="index">the tag index</param>
        /// <param name="filter">the filter</param>
        public void Tag(int index, Filter filter)
        {
            this.Tags[index] = filter;
        }

        public void UnTag(int index)
        {
            this.Tags.Remove(index);
        }

        // True if the view is in a progress of something
        public bool IsInProgress { get; private set; }
        public ProgressItem CurrentProgress { get; private set; } = new ProgressItem("Ready");

        /// <summary>
        /// Find the next occurrence. Yielding progress from 0 to 100, if -1 is yielded, it means no result till the end of the current direction.
        /// Continue iterating the progress will search from the other end. If -2 is yielded, it means no result for all items.
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="currentIndex"></param>
        /// <param name="direction"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        public IEnumerable<int> Find(Filter filter, int currentIndex, bool direction)
        {
            if (!this.IsInitialized || this.IsInProgress) yield break;

            this.OnReportStart("Searching");
            yield return 0;

            int total = this.ItemIndexes?.Count ?? this.Data.Items.Count;

            bool loopedBack = false;
            for (int i = 1; i < total; i++)
            {
                if (i % (total / 20) == 0)
                {
                    int progress = 5 + ((i * 100) / total) * 90 / 100;
                    yield return progress;
                    this.OnReportProgress(progress);
                }

                int logicalIndex = (currentIndex + (direction ? i : -i) + total) % (total);
                if (!loopedBack && (direction ^ currentIndex < logicalIndex))
                {
                    loopedBack = true;
                    yield return -1;
                }

                int index = this.GetPhysicalIndex(logicalIndex);

                if (filter.Match<T>(this.Data.Items[index], this.Data.Templates[this.Data.Items[index].TemplateId]))
                {
                    this.SelectedRowIndex = logicalIndex;
                    break;
                }
            }

            yield return 100;
            this.OnReportFinish();
        }

        public IEnumerable<int> Count(Filter filter)
        {
            if (!this.IsInitialized || this.IsInProgress) yield break;

            this.OnReportStart("Counting");
            yield return 0;

            int total = this.TotalCount;
            int count = 0;

            for (int i = 0; i < total; i++)
            {
                if (i % (total / 20) == 0)
                {
                    int progress = 5 + ((i * 100) / total) * 90 / 100;
                    yield return progress;
                    this.OnReportProgress(progress);
                }

                int index = this.GetPhysicalIndex(i);

                if (filter.Match<T>(this.Data.Items[index], this.Data.Templates[this.Data.Items[index].TemplateId]))
                {
                    count++;
                }
            }

            this.LastCountResult = count;

            yield return 100;
            this.OnReportFinish();
        }

        public int? LastCountResult { get; private set; }

        public IEnumerable<int> Initialize()
        {
            if (this.IsInitialized || this.IsInProgress) yield break;

            this.OnReportStart("Initializing");
            yield return 0;

            if (this.Parent == null)
            {
                foreach (int progress in this.Data.Load(this.Filter))
                {
                    int p = progress * 90 / 100;
                    this.OnReportProgress(p);
                    yield return p;
                }

                this.Data.ItemAdded += (s, e) =>
                {
                    this.OnItemAdded(e);
                };
            }
            else
            {
                int total = this.Parent.TotalCount;

                for (int i = 0; i < total; i++)
                {
                    if (i % (total / 20) == 0)
                    {
                        int progress = ((i * 100) / total) * 90 / 100;
                        yield return progress;
                        this.OnReportProgress(progress);
                    }

                    int index = this.Parent.GetPhysicalIndex(i);

                    if (this.Filter.Match<T>(this.Data.Items[index], this.Data.Templates[this.Data.Items[index].TemplateId]))
                    {
                        this.ItemIndexes.Add(index);
                    }
                }
            }

            if (this.TotalCount > 0)
            {
                int firstIndex = this.GetPhysicalIndex(0), lastIndex = this.GetPhysicalIndex(this.TotalCount - 1);
                this.Statistics.SetFirstLast(this.Data.Items[firstIndex], this.Data.Items[lastIndex]);

                for (int i = 0; i < this.TotalCount; i++)
                {
                    int index = this.GetPhysicalIndex(i);
                    this.Statistics.Sample(this.Data.Items[index], this.Data.Templates[this.Data.Items[index].TemplateId]);
                }
            }

            this.IsInitialized = true;

            yield return 100;
            this.OnReportFinish();
        }

        public bool IsInitialized { get; private set; }

        #endregion

        #region Events

        public event EventHandler<int> ItemAdded;

        #endregion

        #region Child

        public IFilteredView<T> CreateChild(Filter filter)
        {
            return new FilteredView<T>(filter, this, this.Data);
        }

        #endregion

        #region Display

        public event EventHandler<ProgressItem> ProgressChanged;
        public int? FirstDisplayedScrollingRowIndex { get; set; }
        public int? SelectedRowIndex { get; set; }

        public string Name { get; private set; }

        public int TotalCount { get { return this.ItemIndexes?.Count ?? this.Data.Items.Count; } }

        public T GetRowValue(int rowIndex)
        {
            if (rowIndex >= this.TotalCount) return null;
            int index = this.GetPhysicalIndex(rowIndex);
            return this.Data.Items[index];
        }

        public object GetColumnValue(int rowIndex, int columnIndex)
        {
            //            if (columnIndex == 4) return null;
            //          if (columnIndex == 5) return null;
            //        return rowIndex;

            if (rowIndex >= this.TotalCount) return null;
            int index = this.GetPhysicalIndex(rowIndex);

            // only process the tag column
            if (string.Equals(this.Data.ColumnInfos[columnIndex].Name, "Tag"))
            {
                T item = this.Data.Items[index];
                return this.Tags?.Where(kvp => kvp.Value.Match(item, this.Data.Templates[item.TemplateId])).Select(kvp => kvp.Key).ToList();
            }

            return this.Data.GetColumnValue(index, columnIndex);
        }

        public int GetPhysicalIndex(int logicalIndex)
        {
            return this.ItemIndexes?[logicalIndex] ?? logicalIndex;
        }

        public IReadOnlyList<ColumnInfoAttribute> ColumnInfos { get { return this.Data.ColumnInfos; } }

        public IReadOnlyList<string> Templates { get { return this.Data.Templates; } }
        public FilteredViewStatistics Statistics { get; private set; } = new FilteredViewStatistics();

        #endregion

        #region Constructors

        internal FilteredView(Filter filter, FilteredView<T> parent, ILogSource<T> data) : this(filter.Name)
        {
            this.Filter = filter;
            this.Parent = parent;
            this.Data = data;
            this.ItemIndexes = new List<int>();

            if (this.Parent != null)
            {
                this.Parent.ItemAdded += (s, e) =>
                {
                    if (this.IsInitialized && filter.Match(this.Data.Items[e], this.Data.Templates[this.Data.Items[e].TemplateId]))
                    {
                        this.AddItem(e);
                    }
                };
            }
        }

        protected FilteredView(string name)
        {
            this.Name = name;
        }

        #endregion

        #region Properties and fields

        private IDictionary<int, Filter> Tags { get; set; } = new Dictionary<int, Filter>();

        private Filter Filter { get; set; }

        private FilteredView<T> Parent { get; set; }

        internal ILogSource<T> Data { get; set; }

        private List<int> ItemIndexes { get; set; }

        #endregion

        #region Private methods

        protected void OnItemAdded(int index)
        {
            this.ItemAdded?.Invoke(this, index);
        }

        private void AddItem(int index)
        {
            this.ItemIndexes.Add(index);

            // passing the raw index directly to the child for performance.
            this.OnItemAdded(index);
        }

        private void OnReportStart(string actionName)
        {
            this.IsInProgress = true;
            this.CurrentProgress = new ProgressItem(actionName);
            this.OnReport(this.CurrentProgress);
        }

        private void OnReportFinish()
        {
            this.IsInProgress = false;
            this.CurrentProgress = new ProgressItem("Ready") { Progress = 100 };
            this.OnReport(this.CurrentProgress);
        }

        private void OnReportProgress(int progress)
        {
            this.CurrentProgress.Progress = progress;
            this.OnReport(this.CurrentProgress);
        }

        private void OnReport(ProgressItem item)
        {
            this.ProgressChanged?.Invoke(this, item);
        }

        #endregion
    }
}