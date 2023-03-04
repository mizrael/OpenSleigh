#!/bin/bash
echo "sleeping for 10 seconds"
sleep 10

echo mongo_setup.sh time now: `date +"%T" `
mongosh --host openSleigh.tests.infrastructure.mongodb:27017 <<EOF
  var cfg = {
    "_id": "opensleigh",
    "version": 1,
    "members": [
      {
        "_id": 0,
        "host": "openSleigh.tests.infrastructure.mongodb:27017",
        "priority": 2
      }
    ]
  };
  rs.initiate(cfg);
EOF