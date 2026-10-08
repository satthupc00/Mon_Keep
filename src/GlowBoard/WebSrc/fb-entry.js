// Chỉ lấy các hàm Firebase app cần dùng, gói lại thành window.FB
import { initializeApp } from "firebase/app";
import {
  initializeAuth, indexedDBLocalPersistence, browserLocalPersistence, onAuthStateChanged,
  signInAnonymously, signInWithEmailAndPassword, signOut, connectAuthEmulator,
} from "firebase/auth";
import {
  initializeFirestore, persistentLocalCache, persistentSingleTabManager, connectFirestoreEmulator,
  doc, collection, query, where, orderBy, getDoc, getDocs, setDoc, updateDoc, deleteDoc,
  onSnapshot, writeBatch, serverTimestamp, getCountFromServer, Timestamp,
} from "firebase/firestore";

window.FB = {
  initializeApp,
  initializeAuth, indexedDBLocalPersistence, browserLocalPersistence, onAuthStateChanged,
  signInAnonymously, signInWithEmailAndPassword, signOut, connectAuthEmulator,
  initializeFirestore, persistentLocalCache, persistentSingleTabManager, connectFirestoreEmulator,
  doc, collection, query, where, orderBy, getDoc, getDocs, setDoc, updateDoc, deleteDoc,
  onSnapshot, writeBatch, serverTimestamp, getCountFromServer, Timestamp,
};
