# JSONToJavaScript API (.NET)

This API accepts one or many JSON report payloads and converts each into JavaScript lines.

## Run

```bash
dotnet restore
dotnet run
```

## Endpoint

- **POST** `/api/convert`
- Body: raw JSON object(s) or escaped JSON string.

## Example response

```json
{
  "objectCount": 1,
  "lines": [
    "var tableMetaDataJson = {",
    "    name: \"Order\"",
    "};"
  ],
  "javaScript": "var tableMetaDataJson = ..."
}
```
