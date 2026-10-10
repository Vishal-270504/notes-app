# API Rate Limiting

## What is Rate Limiting?
Rate limiting is a strategy for limiting network traffic. It puts a cap on how often someone can repeat an action within a certain timeframe – for example, limiting an IP address to 10 requests per minute.

### Why use it?
1. **Prevent Abuse**: Protects the API from malicious attacks (DoS/DDoS) and brute-force attempts.
2. **Cost Management**: Prevents unexpected infrastructure costs from traffic spikes.
3. **Fair Usage**: Ensures one user doesn't hog all resources, keeping the API responsive for everyone.
4. **Stability**: Helps maintain consistent performance under heavy load.

## Implementation Details
In this project, the `NotesApi` uses a **Fixed Window Limiter**:
- **Route**: All `/notes` endpoints (GET, POST, DELETE).
- **Limit**: 10 requests per minute.
- **Scope**: Per IP address.
- **Response**: Returns `429 Too Many Requests` with a JSON body when the limit is hit.

## How to Test
You can test the rate limit using `curl` or any API client like Postman.

### Using Bash/Terminal:
Run the following command to send 11 requests quickly. The 11th request will fail with a 429 status.

```bash
for i in {1..11}; do 
  echo -n "Request $i: "
  curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/notes
done
```

### Expected Output:
- Requests 1-10: `200`
- Request 11: `429`

### Viewing the Error Message:
```bash
curl -i http://localhost:5000/notes
```
If limited, you will see:
```json
{
  "error": "Too Many Requests",
  "message": "Rate limit exceeded: 10 requests per minute per IP on /notes"
}
```
