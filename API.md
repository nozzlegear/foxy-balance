# Foxy Balance API Documentation

The Foxy Balance API is a RESTful API that uses JSON for request and response bodies. All responses follow the HAL (Hypertext Application Language) format, which includes hypermedia links to related resources.

## Base URL

```
https://www.foxybalance.com
```

## Authentication

The API uses Bearer token authentication with JWT (JSON Web Tokens). To access protected endpoints, you must:

1. Obtain an API key and secret from the web interface
2. Exchange your API credentials for an access token
3. Include the access token in the `Authorization` header of subsequent requests

### Obtain Access Token

Exchange your API key and secret for access and refresh tokens.

**Endpoint:** `POST /api/v1/auth/token`

**Request Body:**
```json
{
  "apiKey": "your-api-key",
  "apiSecret": "your-api-secret"
}
```

**Example:**
```bash
curl -X POST https://www.foxybalance.com/api/v1/auth/token \
  -H "Content-Type: application/json" \
  -d '{
    "apiKey": "fb_1a2b3c4d5e6f",
    "apiSecret": "secret_abcdef123456"
  }'
```

**Response:**
```json
{
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "a1b2c3d4e5f6...",
    "expiresIn": 3600,
    "tokenType": "Bearer"
  },
  "links": {
    "self": { "href": "/api/v1/auth/token" },
    "token-refresh": { "href": "/api/v1/auth/refresh", "method": "POST" },
    "balance": { "href": "/api/v1/balance" },
    "transactions": { "href": "/api/v1/transactions" },
    "bills": { "href": "/api/v1/bills" }
  }
}
```

### Refresh Access Token

Exchange a refresh token for new access and refresh tokens. Refresh tokens are single-use and expire after being consumed.

**Endpoint:** `POST /api/v1/auth/refresh`

**Request Body:**
```json
{
  "refreshToken": "your-refresh-token"
}
```

**Example:**
```bash
curl -X POST https://www.foxybalance.com/api/v1/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{
    "refreshToken": "a1b2c3d4e5f6..."
  }'
```

**Response:**
```json
{
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "x9y8z7w6v5u4...",
    "expiresIn": 3600,
    "tokenType": "Bearer"
  },
  "links": {
    "self": { "href": "/api/v1/auth/refresh" },
    "token-refresh": { "href": "/api/v1/auth/refresh", "method": "POST" },
    "balance": { "href": "/api/v1/balance" }
  }
}
```

## Balance

### Get Balance Summary

Get a summary of the user's current balance, including pending and cleared transactions.

**Endpoint:** `GET /api/v1/balance`

**Authentication:** Required

**Example:**
```bash
curl https://www.foxybalance.com/api/v1/balance \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

**Response:**
```json
{
  "data": {
    "sum": 1250.50,
    "pendingSum": 1100.00,
    "clearedSum": 150.50,
    "transactionCount": 42
  },
  "links": {
    "self": { "href": "/api/v1/balance" },
    "transactions": { "href": "/api/v1/transactions" }
  }
}
```

## Transactions

### List Transactions

List transactions with optional pagination and status filtering.

**Endpoint:** `GET /api/v1/transactions`

**Authentication:** Required

**Query Parameters:**
- `page` (optional, default: 1): Page number for pagination
- `status` (optional): Filter by status - `pending`, `cleared`, or omit for all transactions

**Example:**
```bash
# Get all transactions (first page)
curl https://www.foxybalance.com/api/v1/transactions \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."

# Get pending transactions
curl "https://www.foxybalance.com/api/v1/transactions?status=pending" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."

# Get page 2 of cleared transactions
curl "https://www.foxybalance.com/api/v1/transactions?page=2&status=cleared" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

**Response:**
```json
{
  "data": {
    "items": [
      {
        "data": {
          "id": 123,
          "name": "Grocery Store",
          "amount": -45.67,
          "type": "debit",
          "status": "cleared",
          "date": "2024-01-15",
          "clearDate": "2024-01-16",
          "checkNumber": null,
          "recurringBillId": null
        },
        "links": {
          "self": { "href": "/api/v1/transactions/123" }
        }
      }
    ],
    "page": 1,
    "totalPages": 3,
    "totalCount": 125
  },
  "links": {
    "self": { "href": "/api/v1/transactions?page=1" },
    "next": { "href": "/api/v1/transactions?page=2" },
    "create": { "href": "/api/v1/transactions", "method": "POST" },
    "import": { "href": "/api/v1/transactions/import", "method": "POST" },
    "balance": { "href": "/api/v1/balance" }
  }
}
```

### Get Single Transaction

Retrieve details for a specific transaction.

**Endpoint:** `GET /api/v1/transactions/{id}`

**Authentication:** Required

**Example:**
```bash
curl https://www.foxybalance.com/api/v1/transactions/123 \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

**Response:**
```json
{
  "data": {
    "id": 123,
    "name": "Grocery Store",
    "amount": -45.67,
    "type": "debit",
    "status": "cleared",
    "date": "2024-01-15",
    "clearDate": "2024-01-16",
    "checkNumber": null,
    "recurringBillId": null
  },
  "links": {
    "self": { "href": "/api/v1/transactions/123" },
    "update": { "href": "/api/v1/transactions/123", "method": "PUT" },
    "delete": { "href": "/api/v1/transactions/123", "method": "DELETE" }
  }
}
```

### Create Transaction

Create a new transaction.

**Endpoint:** `POST /api/v1/transactions`

**Authentication:** Required

**Request Body:**
```json
{
  "name": "Transaction name",
  "amount": "100.00",
  "transactionType": "debit",
  "date": "2024-01-15",
  "clearDate": "",
  "checkNumber": ""
}
```

**Field Descriptions:**
- `name` (required): Description of the transaction
- `amount` (required): Amount as a string (positive for debits, negative for credits)
- `transactionType` (required): Either `"debit"` or `"credit"`
- `date` (required): Transaction date in ISO 8601 format (YYYY-MM-DD)
- `clearDate` (optional): Date the transaction cleared (YYYY-MM-DD)
- `checkNumber` (optional): Check number if applicable

**Example:**
```bash
curl -X POST https://www.foxybalance.com/api/v1/transactions \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Coffee Shop",
    "amount": "5.50",
    "transactionType": "debit",
    "date": "2024-01-15",
    "clearDate": "2024-01-16",
    "checkNumber": ""
  }'
```

**Response:** `201 Created`
```json
{
  "data": {
    "id": 124,
    "name": "Coffee Shop",
    "amount": -5.50,
    "type": "debit",
    "status": "cleared",
    "date": "2024-01-15",
    "clearDate": "2024-01-16",
    "checkNumber": null,
    "recurringBillId": null
  },
  "links": {
    "self": { "href": "/api/v1/transactions/124" }
  }
}
```

### Update Transaction

Update an existing transaction.

**Endpoint:** `PUT /api/v1/transactions/{id}`

**Authentication:** Required

**Request Body:** Same format as Create Transaction

**Example:**
```bash
curl -X PUT https://www.foxybalance.com/api/v1/transactions/124 \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Coffee Shop (Updated)",
    "amount": "6.00",
    "transactionType": "debit",
    "date": "2024-01-15",
    "clearDate": "2024-01-16",
    "checkNumber": ""
  }'
```

**Response:**
```json
{
  "data": {
    "id": 124,
    "name": "Coffee Shop (Updated)",
    "amount": -6.00,
    "type": "debit",
    "status": "cleared",
    "date": "2024-01-15",
    "clearDate": "2024-01-16",
    "checkNumber": null,
    "recurringBillId": null
  },
  "links": {
    "self": { "href": "/api/v1/transactions/124" }
  }
}
```

### Delete Transaction

Delete a transaction.

**Endpoint:** `DELETE /api/v1/transactions/{id}`

**Authentication:** Required

**Example:**
```bash
curl -X DELETE https://www.foxybalance.com/api/v1/transactions/124 \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

**Response:** `204 No Content`

### Import Transactions

Bulk import transactions from a CSV file.

**Endpoint:** `POST /api/v1/transactions/import`

**Authentication:** Required

**Request Body:**
```json
{
  "format": "capital-one",
  "transactions": "Transaction Date,Posted Date,Card No.,Description,Category,Debit,Credit\n2024-01-15,2024-01-16,1234,GROCERY STORE,Groceries,45.67,\n2024-01-14,2024-01-15,1234,GAS STATION,Automotive,52.00,"
}
```

**Field Descriptions:**
- `format` (required): Import format. Currently supported: `"capital-one"`
- `transactions` (required): CSV data as a string

**Example:**
```bash
curl -X POST https://www.foxybalance.com/api/v1/transactions/import \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "Content-Type: application/json" \
  -d '{
    "format": "capital-one",
    "transactions": "Transaction Date,Posted Date,Card No.,Description,Category,Debit,Credit\n2024-01-15,2024-01-16,1234,GROCERY STORE,Groceries,45.67,\n2024-01-14,2024-01-15,1234,GAS STATION,Automotive,52.00,"
  }'
```

**Response:**
```json
{
  "data": {
    "importedCount": 2,
    "totalCount": 2,
    "skippedCount": 0
  },
  "links": {
    "self": { "href": "/api/v1/transactions/import" },
    "transactions": { "href": "/api/v1/transactions" }
  }
}
```

## Recurring Bills

### List Recurring Bills

List all recurring bills for the authenticated user.

**Endpoint:** `GET /api/v1/bills`

**Authentication:** Required

**Query Parameters:**
- `active` (optional): Filter by active status - `true` for active bills only, omit for all bills

**Example:**
```bash
# Get all bills
curl https://www.foxybalance.com/api/v1/bills \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."

# Get only active bills
curl "https://www.foxybalance.com/api/v1/bills?active=true" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

**Response:**
```json
{
  "data": {
    "items": [
      {
        "data": {
          "id": 1,
          "name": "Electric Bill",
          "amount": 150.00,
          "scheduleType": "week",
          "weekOfMonth": 1,
          "dayOfWeek": 1,
          "dayOfMonth": null,
          "active": true
        },
        "links": {
          "self": { "href": "/api/v1/bills/1" }
        }
      },
      {
        "data": {
          "id": 2,
          "name": "Rent",
          "amount": 1500.00,
          "scheduleType": "date",
          "weekOfMonth": null,
          "dayOfWeek": null,
          "dayOfMonth": 1,
          "active": true
        },
        "links": {
          "self": { "href": "/api/v1/bills/2" }
        }
      }
    ],
    "page": 1,
    "totalPages": 1,
    "totalCount": 2
  },
  "links": {
    "self": { "href": "/api/v1/bills?active=false" },
    "create": { "href": "/api/v1/bills", "method": "POST" },
    "match-suggestions": { "href": "/api/v1/bills/match/suggestions" },
    "balance": { "href": "/api/v1/balance" }
  }
}
```

### Get Single Bill

Retrieve details for a specific recurring bill.

**Endpoint:** `GET /api/v1/bills/{id}`

**Authentication:** Required

**Example:**
```bash
curl https://www.foxybalance.com/api/v1/bills/1 \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

**Response:**
```json
{
  "data": {
    "id": 1,
    "name": "Electric Bill",
    "amount": 150.00,
    "scheduleType": "week",
    "weekOfMonth": 1,
    "dayOfWeek": 1,
    "dayOfMonth": null,
    "active": true
  },
  "links": {
    "self": { "href": "/api/v1/bills/1" },
    "update": { "href": "/api/v1/bills/1", "method": "PUT" },
    "delete": { "href": "/api/v1/bills/1", "method": "DELETE" },
    "toggle-active": { "href": "/api/v1/bills/1/toggle-active", "method": "POST" }
  }
}
```

### Create Recurring Bill

Create a new recurring bill. Bills can be scheduled using either week-based (e.g., "2nd Wednesday") or date-based (e.g., "15th of month") patterns.

**Endpoint:** `POST /api/v1/bills`

**Authentication:** Required

**Request Body (Week-Based Schedule):**
```json
{
  "name": "Electric Bill",
  "amount": "150.00",
  "scheduleType": "week",
  "weekOfMonth": "1",
  "dayOfWeek": "1",
  "dayOfMonth": ""
}
```

**Request Body (Date-Based Schedule):**
```json
{
  "name": "Rent",
  "amount": "1500.00",
  "scheduleType": "date",
  "weekOfMonth": "",
  "dayOfWeek": "",
  "dayOfMonth": "1"
}
```

**Field Descriptions:**
- `name` (required): Name/description of the bill
- `amount` (required): Expected bill amount as a string
- `scheduleType` (required): Either `"week"` for week-based or `"date"` for date-based scheduling
- `weekOfMonth` (required for week-based): Week of the month (1-4) when the bill is due
- `dayOfWeek` (required for week-based): Day of the week (0-6, where 0 is Sunday) when the bill is due
- `dayOfMonth` (required for date-based): Day of the month (1-31) when the bill is due. If the day doesn't exist in a month (e.g., 31st in February), the bill will be applied on the last day of that month.

**Example (Week-Based):**
```bash
curl -X POST https://www.foxybalance.com/api/v1/bills \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Internet Bill",
    "amount": "79.99",
    "scheduleType": "week",
    "weekOfMonth": "2",
    "dayOfWeek": "3",
    "dayOfMonth": ""
  }'
```

**Example (Date-Based):**
```bash
curl -X POST https://www.foxybalance.com/api/v1/bills \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Mortgage",
    "amount": "1850.00",
    "scheduleType": "date",
    "weekOfMonth": "",
    "dayOfWeek": "",
    "dayOfMonth": "15"
  }'
```

**Response:** `201 Created`
```json
{
  "data": {
    "id": 2,
    "name": "Internet Bill",
    "amount": 79.99,
    "scheduleType": "week",
    "weekOfMonth": 2,
    "dayOfWeek": 3,
    "dayOfMonth": null,
    "active": true
  },
  "links": {
    "self": { "href": "/api/v1/bills/2" }
  }
}
```

### Update Recurring Bill

Update an existing recurring bill.

**Endpoint:** `PUT /api/v1/bills/{id}`

**Authentication:** Required

**Request Body:** Same format as Create Recurring Bill

**Example:**
```bash
curl -X PUT https://www.foxybalance.com/api/v1/bills/2 \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Internet Bill (Updated)",
    "amount": "89.99",
    "weekOfMonth": "2",
    "dayOfWeek": "3"
  }'
```

**Response:**
```json
{
  "data": {
    "id": 2,
    "name": "Internet Bill (Updated)",
    "amount": 89.99,
    "scheduleType": "week",
    "weekOfMonth": 2,
    "dayOfWeek": 3,
    "dayOfMonth": null,
    "active": true
  },
  "links": {
    "self": { "href": "/api/v1/bills/2" }
  }
}
```

### Delete Recurring Bill

Delete a recurring bill.

**Endpoint:** `DELETE /api/v1/bills/{id}`

**Authentication:** Required

**Example:**
```bash
curl -X DELETE https://www.foxybalance.com/api/v1/bills/2 \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

**Response:** `204 No Content`

### Toggle Bill Active Status

Toggle whether a bill is active or inactive. Inactive bills won't generate automatic suggestions or transactions.

**Endpoint:** `POST /api/v1/bills/{id}/toggle-active`

**Authentication:** Required

**Example:**
```bash
curl -X POST https://www.foxybalance.com/api/v1/bills/1/toggle-active \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "Content-Type: application/json" \
  -d '{}'
```

**Response:**
```json
{
  "data": {
    "id": 1,
    "name": "Electric Bill",
    "amount": 150.00,
    "scheduleType": "week",
    "weekOfMonth": 1,
    "dayOfWeek": 1,
    "dayOfMonth": null,
    "active": false
  },
  "links": {
    "self": { "href": "/api/v1/bills/1" }
  }
}
```

## Bill Matching

### Get Match Suggestions

Get automated suggestions for matching imported transactions to recurring bills based on name similarity and amount.

**Endpoint:** `GET /api/v1/bills/match/suggestions`

**Authentication:** Required

**Example:**
```bash
curl https://www.foxybalance.com/api/v1/bills/match/suggestions \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

**Response:**
```json
{
  "data": {
    "items": [
      {
        "data": {
          "transaction": {
            "id": 123,
            "name": "ELECTRIC COMPANY PAYMENT",
            "amount": -150.00,
            "type": "debit",
            "status": "cleared",
            "date": "2024-01-08",
            "clearDate": "2024-01-08",
            "checkNumber": null,
            "recurringBillId": null
          },
          "recurringBill": {
            "id": 1,
            "name": "Electric Bill",
            "amount": 150.00,
            "scheduleType": "week",
            "weekOfMonth": 1,
            "dayOfWeek": 1,
            "dayOfMonth": null,
            "active": true
          },
          "matchScore": 0.85
        },
        "links": {
          "execute-match": { "href": "/api/v1/bills/match", "method": "POST" },
          "transaction:123": { "href": "/api/v1/transactions/123" },
          "bill:1": { "href": "/api/v1/bills/1" }
        }
      }
    ],
    "page": 1,
    "totalPages": 1,
    "totalCount": 1
  },
  "links": {
    "self": { "href": "/api/v1/bills/match/suggestions" },
    "execute-match": { "href": "/api/v1/bills/match", "method": "POST" },
    "bills": { "href": "/api/v1/bills" },
    "transactions": { "href": "/api/v1/transactions" }
  }
}
```

### Execute Match

Link a transaction to a recurring bill. This marks the transaction as being an instance of that bill.

**Endpoint:** `POST /api/v1/bills/match`

**Authentication:** Required

**Request Body:**
```json
{
  "transactionId": 123,
  "billId": 1
}
```

**Example:**
```bash
curl -X POST https://www.foxybalance.com/api/v1/bills/match \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "Content-Type: application/json" \
  -d '{
    "transactionId": 123,
    "billId": 1
  }'
```

**Response:**
```json
{
  "data": {
    "id": 123,
    "name": "ELECTRIC COMPANY PAYMENT",
    "amount": -150.00,
    "type": "debit",
    "status": "cleared",
    "date": "2024-01-08",
    "clearDate": "2024-01-08",
    "checkNumber": null,
    "recurringBillId": 1
  },
  "links": {
    "self": { "href": "/api/v1/transactions/123" },
    "bill:1": { "href": "/api/v1/bills/1" },
    "match-suggestions": { "href": "/api/v1/bills/match/suggestions" }
  }
}
```

## Error Responses

All error responses follow a consistent format:

### Validation Error (422 Unprocessable Entity)
```json
{
  "error": "Validation error message describing what went wrong"
}
```

### Unauthorized (401)
```json
{
  "error": "Invalid API credentials"
}
```

### Not Found (404)
```json
{
  "error": "Transaction not found"
}
```

### Generic Error (500)
```json
{
  "error": "An unexpected error occurred"
}
```

## HAL (Hypertext Application Language)

All successful API responses use the HAL format, which includes:

- `data`: The actual response data
- `links`: Hypermedia links to related resources and available actions

Links follow this structure:
```json
{
  "href": "/api/v1/resource",
  "method": "GET"
}
```

The `method` field is optional and defaults to `GET` when not specified.

## Rate Limiting

There are currently no rate limits enforced on the API, but this may change in future versions.

## Notes

- All timestamps are in ISO 8601 format
- Amounts are represented as decimals with up to 2 decimal places
- Transaction amounts are negative for debits (money out) and positive for credits (money in)
- The API uses optimistic concurrency - the last write wins
- All endpoints require authentication except for the authentication endpoints themselves
