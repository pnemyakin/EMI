// Устаревшие тесты NGCArray удалены — пул переведён на ArrayPool<T>.Shared,
// который не поддерживает семантику reference-equality, best-fit и FreeArraysCount.
// Актуальные тесты в Test_NGCArray_Extended.cs.