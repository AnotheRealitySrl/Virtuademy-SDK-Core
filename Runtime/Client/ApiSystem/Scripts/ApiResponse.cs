using Newtonsoft.Json;


using System;

using UnityEngine;


using SPACS.Utilities;

namespace Virtuademy.SDK.Http
{
    [Serializable, JsonObject(MemberSerialization.Fields)]
    public class ApiResponse
    {
        [SerializeField] private int statusCode;
        [SerializeField] private string reasonPhrase;
        [SerializeField] int content;

        public bool IsSuccess { get => (statusCode >= 200) && (statusCode <= 299); }
        public long StatusCode { get => statusCode; private set => statusCode = (int)value; }
        public string ReasonPhrase { get => reasonPhrase; private set => reasonPhrase = value; }
        public int Content { get => content; set => content = value; }

        public ApiResponse(long statusCode, string reasonPhrase, string content)
        {
            StatusCode = statusCode;
            ReasonPhrase = reasonPhrase;
            Content = IsSuccess ? int.TryParse(content, out int value) ? value : -1 : -1;
        }
    }

    [Serializable]
    public class ApiResponse<T>
    {
        [SerializeField] private int statusCode;
        [SerializeField] private string reasonPhrase;
        [SerializeField] T content;

        public bool IsSuccess { get => (statusCode >= 200) && (statusCode <= 299); }
        public long StatusCode { get => statusCode; private set => statusCode = (int)value; }
        public string ReasonPhrase { get => reasonPhrase; private set => reasonPhrase = value; }
        public T Content { get => content; set => content = value; }

        public ApiResponse(long statusCode, string reasonPhrase, string content)
        {
            StatusCode = statusCode;
            if (!((statusCode >= 200) && (statusCode <= 299)) && !string.IsNullOrWhiteSpace(content))
            {
                JsonConvert.DeserializeObject<ApiResponseError>(content).DisplayError();
            }
            ReasonPhrase = reasonPhrase;
            if (typeof(T) == typeof(string))
            {
                Content = IsSuccess ? (T)(object)content : default;
            }
            else
            {
                Content = IsSuccess ? JsonConvert.DeserializeObject<T>(content) : default;
            }
        }
    }

    [Serializable]
    public class ApiResponseArray<T>
    {
        [SerializeField] private int statusCode;
        [SerializeField] private string reasonPhrase;
        [SerializeField] T[] content;
        [SerializeField] int totalCount;

        public bool IsSuccess { get => (statusCode >= 200) && (statusCode <= 299); }
        public long StatusCode { get => statusCode; private set => statusCode = (int)value; }
        public string ReasonPhrase { get => reasonPhrase; private set => reasonPhrase = value; }
        public T[] Content { get => content; private set => content = value; }
        public int TotalCount { get => totalCount; private set => totalCount = value; }

        public ApiResponseArray(long statusCode, string reasonPhrase, string content, int totalCount = -1)
        {
            StatusCode = statusCode;
            if (!((statusCode >= 200) && (statusCode <= 299)) && !string.IsNullOrWhiteSpace(content))
            {
                JsonConvert.DeserializeObject<ApiResponseError>(content).DisplayError();
            }
            ReasonPhrase = reasonPhrase;

            Content = IsSuccess ? JsonArrayHelper.FromJson<T>(content) : null;

            TotalCount = totalCount;
        }
    }
    [Serializable]
    public class ApiResponseSearch<T>
    {
        [SerializeField] private int statusCode;
        [SerializeField] private string reasonPhrase;
        [SerializeField] private ContentSearch content;

        public bool IsSuccess { get => (statusCode >= 200) && (statusCode <= 299); }
        public long StatusCode { get => statusCode; private set => statusCode = (int)value; }
        public string ReasonPhrase { get => reasonPhrase; private set => reasonPhrase = value; }
        public ContentSearch Content { get => content; set => content = value; }

        public ApiResponseSearch(long statusCode, string reasonPhrase, string content)
        {
            StatusCode = statusCode;
            if (!((statusCode >= 200) && (statusCode <= 299)) && !string.IsNullOrWhiteSpace(content))
            {
                JsonConvert.DeserializeObject<ApiResponseError>(content).DisplayError();
            }
            ReasonPhrase = reasonPhrase;
            Content = IsSuccess ? JsonConvert.DeserializeObject<ContentSearch>(content) : null;
        }
        [Serializable]
        public class ContentSearch
        {
            [SerializeField] private T[] data;
            [SerializeField] private int totalCount;
            [SerializeField] private int pageSize;
            [SerializeField] private int currentPage;
            [SerializeField] private string order;
            [SerializeField] private string dbOrder;
            [SerializeField] private string validationError;

            public T[] Data { get => data; set => data = value; }
            public int TotalCount { get => totalCount; set => totalCount = value; }
            public int PageSize { get => pageSize; set => pageSize = value; }
            public int CurrentPage { get => currentPage; set => currentPage = value; }
            public string Order { get => order; set => order = value; }
            public string DbOrder { get => dbOrder; set => dbOrder = value; }
            public string ValidationError { get => validationError; set => validationError = value; }
        }
    }

    [Serializable]
    [Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.Fields)]
    public class ApiResponseError
    {
        private string type;
        private string title;
        private int status;
        private string traceId;

        public string Type { get => type; set => type = value; }
        public string Title { get => title; set => title = value; }
        public int Status { get => status; set => status = value; }
        public string TraceId { get => traceId; set => traceId = value; }

        public void DisplayError()
        {
            Debug.LogError(Title);
        }
    }
}