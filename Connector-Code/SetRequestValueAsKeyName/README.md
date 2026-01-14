This custom code was developed for a connector for Databox. In order to [send data](https://developers.databox.com/send-data/), you must use a key name that is token/metric-specific, such as:

```json
{
  "data":[
    {
      "$<metric_key_id>": 1
    }
  ]
}
```

You can see that it was also required to put that key in an object, then in added to array called "data" and then wrapped in a JSON object.