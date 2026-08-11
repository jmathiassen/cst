AC_DEFUN([outer],
[
  AC_REQUIRE([a])
  case $x in
    y) z=1 ;;
  esac
  AC_CONFIG_COMMANDS([n], [true], [true])
])
