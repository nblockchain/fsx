all:
	xbuild

install:
	xbuild /p:Configuration=Release
	./install.sh
