# API Design Guidelines & Standards

Conventions for designing, implementing, and consuming APIs across the BrewYou platform, overseen by the **API Contract Reviewer**.

---

## 1. RESTful URI Conventions

- Use nouns in plural form for resource endpoints: `/api/v1/recipes`, `/api/v1/batches`.
- Use nested paths for hierarchical relationships: `/api/v1/batches/{batchId}/fermentations`.
- Use lowercase with hyphens for multi-word paths: `/api/v1/user-profiles`.

---

## 2. HTTP Methods & Status Codes

| Method | Idempotent | Usage | Success Status |
| :--- | :--- | :--- | :--- |
| `GET` | Yes | Retrieve resource(s) | `200 OK` |
| `POST` | No | Create resource or execute non-idempotent action | `201 Created` or `200 OK` |
| `PUT` | Yes | Complete resource replacement | `200 OK` |
| `PATCH`| No | Partial resource update | `200 OK` |
| `DELETE`| Yes | Remove resource | `200 OK` or `204 No Content` |

### Common Error Codes
- `400 Bad Request`: Validation failure or malformed JSON payload.
- `401 Unauthorized`: Authentication missing, expired, or invalid.
- `403 Forbidden`: Authenticated user lacks required permissions.
- `404 Not Found`: Target resource does not exist.
- `409 Conflict`: Unique constraint violation or state conflict.
- `500 Internal Server Error`: Unhandled server exception.

---

## 3. Uniform Response Envelope

All API responses must follow a consistent JSON envelope format:

### Success Response
```json
{
  "success": true,
  "data": {
    "id": "batch_123",
    "name": "IPA Batch #1",
    "status": "fermenting"
  }
}
```

### Error Response
```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Invalid batch creation payload",
    "details": [
      {
        "field": "originalGravity",
        "issue": "Must be greater than 1.000"
      }
    ]
  }
}
```

---

## 4. Pagination & Dates

- **Pagination Query**: `?page=1&limit=20`
- **Pagination Response**: Include metadata:
  ```json
  {
    "success": true,
    "data": [ ... ],
    "pagination": {
      "page": 1,
      "limit": 20,
      "total": 85,
      "totalPages": 5
    }
  }
  ```
- **Dates & Times**: Always serialize timestamps as ISO 8601 strings in UTC (e.g. `2026-09-04T10:45:00.000Z`).
