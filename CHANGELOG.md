# Changelog

All notable changes to this project will be documented in this file.

## [2021-02-24](https://github.com/mizrael/OpenSleigh/pull/27)
### Added
- added support for compensating transactions
- added new sample

## [2021-02-11](https://github.com/mizrael/OpenSleigh/pull/26)
### Added
- added automatic infrastructure provisioning

## [2021-02-09](https://github.com/mizrael/OpenSleigh/pull/25)
### Added
- added support for Azure Service Bus

## [2021-02-04](https://github.com/mizrael/OpenSleigh/pull/24)
### Added
- Sagas can be configured to use a retry policy when handling messages
- added a new sample
- minor bugfixes

## [2021-01-30](https://github.com/mizrael/OpenSleigh/pull/22)
### Added
- added System Info singleton instance
- added possibility to set client as "publish only"
- updated samples
### Fixed
- fixed samples connection strings

## [2021-01-27](https://github.com/mizrael/OpenSleigh/pull/21)
### Added
- multiple Sagas can be registered to handle the same message type
- minor refactorings
- added SQL persistence library
- refactored transaction handling
- updated dependencies lifetimes
- improved test coverage
- minor refactorings and performance improvements

## [2021-01-14](https://github.com/mizrael/OpenSleigh/pull/17)
### Added
- multiple Sagas can be registered to handle the same message type
- minor refactorings

## [2021-01-14](https://github.com/mizrael/OpenSleigh/pull/16)
### Added
- Saga States can now be reconstructed from typed messages

## [2021-01-14](https://github.com/mizrael/OpenSleigh/pull/15)
### Added
- possibility to configure exchange and queue names for each message
- moved from Fanout to Topic exchanges in the RabbitMQ Transport library

## [2021-01-11](https://github.com/mizrael/OpenSleigh/pull/13)
### Added
- added channel pooling
- added RabbitMQ E2E tests with in-memory transport

## [2021-01-11](https://github.com/mizrael/OpenSleigh/pull/14)
### Added
- added in-memory E2E tests

### Fixed
- fixed in-memory state repository, added support for multiple types sharing correlation id 