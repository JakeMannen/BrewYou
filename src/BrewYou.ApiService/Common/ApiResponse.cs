namespace BrewYou.ApiService.Common;

public record ApiResponse<T>(
    bool Success,
    T? Data = default,
    ApiError? Error = null,
    PaginationMeta? Pagination = null
)
{
    public static ApiResponse<T> Ok(T data, PaginationMeta? pagination = null) =>
        new(true, Data: data, Error: null, Pagination: pagination);

    public static ApiResponse<T> Fail(string code, string message, IEnumerable<ApiErrorDetail>? details = null) =>
        new(false, Data: default, Error: new ApiError(code, message, details?.ToList()), Pagination: null);
}

public record ApiResponse(
    bool Success,
    ApiError? Error = null
)
{
    public static ApiResponse Ok() => new(true, null);
    public static ApiResponse Fail(string code, string message, IEnumerable<ApiErrorDetail>? details = null) =>
        new(false, new ApiError(code, message, details?.ToList()));
}

public record ApiError(
    string Code,
    string Message,
    List<ApiErrorDetail>? Details = null
);

public record ApiErrorDetail(
    string Field,
    string Issue
);

public record PaginationMeta(
    int Page,
    int Limit,
    int Total,
    int TotalPages
);

public record PaginationQuery(
    int Page = 1,
    int Limit = 20
);