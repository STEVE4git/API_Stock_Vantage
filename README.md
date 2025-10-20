## **Security concerns**

This program does some very foolish things with regards to API key security (hardcoded in the repo) that should never be done in production

In production, this should be a docker server and stored as a secret. As well as if this was our API, it should have different keys (user/developer/admin) with
varying read/write permissions.


## Usability concerns

The free key updates data at end of day and not every 15 minutes as the premium key does.
