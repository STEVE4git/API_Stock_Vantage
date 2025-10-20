## **Security concerns**

This program does some very foolish things with regards to API key security (hardcoded in the repo) that should never be done in production. I did not go through with the creation of a docker server due to that being outside the scope of what was requested.

In production, this should be a docker server and stored as a secret. As well as if this was our API, it should have different keys (user/developer/admin) with
varying read/write permissions.


## Usability concerns

The free key updates data at end of day and not every 15 minutes as the premium key does.


## Design consideration

I decided to have a main file and a seperate header file. The singular helper file seemed to be the cleanest way to do this as the actual code is very limited and splitting it up into individual files would hurt readability in my opinon rather than improve it. If this was severely expanded, there could be discussions on breaking up the helper file into multiple different sections. 
