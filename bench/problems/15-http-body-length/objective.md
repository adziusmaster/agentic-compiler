## HTTP body length

Fetch a URL and return the number of characters in the response body.

- AGC: declare `(extern defun http_fetch ((url : Str)) : Str @capability "http.fetch")`
  and implement `body_length(url: Str) -> Num`.
- Python: `body_length(url: str, http_get)` where `http_get(url) -> str`.
- An empty body → 0.
