all:
	./scripts/build.sh

release:
	./scripts/build.sh /p:Configuration=Release

install: release
	./scripts/install.sh

check:
	./scripts/runTests.fsx
