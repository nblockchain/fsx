all:
	./scripts/build.sh

release:
	./scripts/build.sh /p:Configuration=Release

install: release
	./scripts/install.sh

reinstall:
	echo "'reinstall' target not supported yet in Unix, uninstall manually and use 'install' for now" >> /dev/stderr
	exit 1

check:
	./scripts/runTests.fsx
