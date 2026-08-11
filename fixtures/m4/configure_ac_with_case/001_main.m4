AC_INIT([pkg], [1.0])
case $host_os in
  mingw*) is_w32=yes ;;
  *) is_w32=no ;;
esac
AC_CONFIG_FILES([Makefile])
AC_CONFIG_COMMANDS([post], [true], [])
